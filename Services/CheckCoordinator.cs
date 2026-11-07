using NyvexGeneration.Core;
using NyvexGeneration.Data;
using NyvexGeneration.Models;
using NyvexGeneration.ViewModels;

namespace NyvexGeneration.Services;

/// <summary>
/// Orquestra consultas: normaliza, valida, remove duplicados, divide em lotes, respeita intervalos,
/// atualiza o estado da sessão e persiste histórico/estatísticas. Não bloqueia a thread de UI.
/// </summary>
public sealed class CheckCoordinator : ICheckCoordinator
{
    private readonly IRobloxApiService _api;
    private readonly IUsernameValidator _validator;
    private readonly SessionState _state;
    private readonly SettingsState _settings;
    private readonly ILogService _log;
    private readonly ILookupHistoryRepository _history;
    private readonly IStatsRepository _stats;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    private int _running;
    private CancellationTokenSource? _cts;

    public CheckCoordinator(
        IRobloxApiService api,
        IUsernameValidator validator,
        SessionState state,
        SettingsState settings,
        ILogService log,
        ILookupHistoryRepository history,
        IStatsRepository stats,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _api = api;
        _validator = validator;
        _state = state;
        _settings = settings;
        _log = log;
        _history = history;
        _stats = stats;
        _delay = delay ?? Task.Delay;
    }

    public bool IsRunning => Volatile.Read(ref _running) == 1;

    public void Cancel()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // execução já terminou
        }
    }

    public async Task<int> RunAsync(IReadOnlyCollection<string> usernames, bool force, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(usernames);

        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            _log.Log(LogSeverity.Warning, "Checker", "Já existe uma consulta em andamento.");
            return 0;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cts = cts;
        var delivered = new List<LookupResult>();
        var begun = false;

        try
        {
            var pending = BuildPending(usernames, force);
            if (pending.Count == 0)
            {
                _log.Log(LogSeverity.Information, "Checker", "Nenhum username novo para consultar.");
                return 0;
            }

            var rows = pending.Select(name => _state.GetOrAdd(name)).ToList();
            foreach (var row in rows)
            {
                _state.MarkChecking(row);
            }

            _state.BeginRun(pending.Count);
            begun = true;
            _log.Log(LogSeverity.Information, "Checker", $"Iniciando consulta de {pending.Count} username(s).");

            try
            {
                await RunChunksAsync(pending, delivered, cts.Token);
            }
            catch (OperationCanceledException)
            {
                _log.Log(LogSeverity.Information, "Checker", "Consulta cancelada pelo usuário.");
            }
            catch (Exception ex)
            {
                _log.Log(LogSeverity.Error, "Checker", "Erro inesperado durante a consulta.", ex.Message);
            }

            foreach (var row in rows.Where(r => r.Status == LookupStatus.Checking))
            {
                _state.ResetPending(row, "Consulta interrompida antes de concluir.");
            }

            return delivered.Count;
        }
        finally
        {
            await PersistAsync(delivered);

            if (begun)
            {
                _state.EndRun();
            }

            _cts = null;
            Volatile.Write(ref _running, 0);
        }
    }

    private List<string> BuildPending(IReadOnlyCollection<string> usernames, bool force)
    {
        var pending = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var invalid = 0;
        var skipped = 0;

        foreach (var raw in usernames)
        {
            var name = raw?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (!_validator.IsValid(name, out _))
            {
                invalid++;
                continue;
            }

            if (!seen.Add(name))
            {
                skipped++;
                continue;
            }

            var existing = _state.Find(name);
            if (!force && existing is { Status: LookupStatus.AccountFound or LookupStatus.NoPublicMatch })
            {
                skipped++;
                continue;
            }

            pending.Add(name);
        }

        if (invalid > 0 || skipped > 0)
        {
            _log.Log(LogSeverity.Information, "Checker",
                $"Ignorados: {invalid} inválido(s), {skipped} duplicado(s) ou já consultado(s).");
        }

        return pending;
    }

    private async Task RunChunksAsync(List<string> pending, List<LookupResult> delivered, CancellationToken token)
    {
        var settings = _settings.Current;
        var batchSize = Math.Clamp(settings.BatchSize, 1, 100);
        var delayMs = settings.DelayBetweenRequestsMs;
        var chunks = pending.Chunk(batchSize).ToList();

        for (var i = 0; i < chunks.Count; i++)
        {
            token.ThrowIfCancellationRequested();

            if (i > 0)
            {
                await _delay(TimeSpan.FromMilliseconds(delayMs), token);
            }

            var chunk = chunks[i];
            IReadOnlyList<LookupResult> results = chunk.Length == 1
                ? new[] { await _api.CheckUsernameAsync(chunk[0], token) }
                : await _api.CheckBatchAsync(chunk, token);

            foreach (var result in results)
            {
                _state.Apply(result);
                delivered.Add(result);
            }

            _state.AdvanceProgress(chunk.Length);
            UpdateConnection(results);

            if (results.Any(r => r.FailureReason == LookupFailureReason.RateLimited))
            {
                delayMs = Math.Min(60_000, Math.Max(delayMs * 2, 5_000));
                _log.Log(LogSeverity.Warning, "Checker", $"Limite detectado: intervalo aumentado para {delayMs} ms.");
            }
        }
    }

    private void UpdateConnection(IReadOnlyList<LookupResult> results)
    {
        if (results.Count == 0)
        {
            return;
        }

        if (results.All(r => r.Status == LookupStatus.ConnectionError))
        {
            _state.Connection = ConnectionState.Offline;
        }
        else if (results.Any(r => r.FailureReason == LookupFailureReason.RateLimited))
        {
            _state.Connection = ConnectionState.RateLimited;
        }
        else if (results.Any(r => r.Status is LookupStatus.AccountFound or LookupStatus.NoPublicMatch))
        {
            _state.Connection = ConnectionState.Online;
        }
    }

    private async Task PersistAsync(List<LookupResult> delivered)
    {
        if (delivered.Count == 0)
        {
            return;
        }

        try
        {
            await _history.SaveAsync(delivered, CancellationToken.None);
            await _stats.AddAsync(StatKeys.AccountsFound, delivered.Count(r => r.Status == LookupStatus.AccountFound), CancellationToken.None);
            await _stats.AddAsync(StatKeys.NoPublicMatch, delivered.Count(r => r.Status == LookupStatus.NoPublicMatch), CancellationToken.None);
            await _stats.AddAsync(StatKeys.ChecksCompleted,
                delivered.Count(r => r.Status is LookupStatus.AccountFound or LookupStatus.NoPublicMatch), CancellationToken.None);
            await _stats.AddAsync(StatKeys.Errors,
                delivered.Count(r => r.Status is LookupStatus.Inconclusive or LookupStatus.ConnectionError), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.Log(LogSeverity.Error, "Database", "Falha ao salvar o histórico de consultas.", ex.Message);
        }
    }
}

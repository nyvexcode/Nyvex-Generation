using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NyvexGeneration.Core;
using NyvexGeneration.Models;

namespace NyvexGeneration.Services;

/// <summary>
/// Consulta pública de usernames via POST users.roblox.com/v1/usernames/users.
/// Não envia cookies nem credenciais. "Nenhuma correspondência" NUNCA significa username livre.
/// </summary>
public sealed class RobloxApiService : IRobloxApiService
{
    public const string Endpoint = "v1/usernames/users";
    public const string ClientName = "roblox";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan MaxRetryWait = TimeSpan.FromSeconds(60);

    private readonly HttpClient _http;
    private readonly ILogService _log;
    private readonly SettingsState _settings;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTimeOffset> _clock;

    public RobloxApiService(
        HttpClient http,
        ILogService log,
        SettingsState settings,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<DateTimeOffset>? clock = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _delay = delay ?? Task.Delay;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public async Task<LookupResult> CheckUsernameAsync(string username, CancellationToken cancellationToken)
    {
        var results = await CheckBatchAsync(new[] { username ?? string.Empty }, cancellationToken).ConfigureAwait(false);

        return results.Count > 0
            ? results[0]
            : new LookupResult(
                username ?? string.Empty, LookupStatus.Inconclusive, LookupFailureReason.None,
                "Username vazio: nada foi consultado.", _clock());
    }

    public async Task<IReadOnlyList<LookupResult>> CheckBatchAsync(
        IReadOnlyCollection<string> usernames, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(usernames);

        var names = usernames
            .Select(n => n?.Trim())
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (names.Count == 0)
        {
            return Array.Empty<LookupResult>();
        }

        var settings = _settings.Current;
        var batchSize = Math.Clamp(settings.BatchSize, 1, 100);
        var chunks = names.Chunk(batchSize).ToList();
        var all = new List<LookupResult>(names.Count);

        for (var i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (i > 0)
            {
                await _delay(TimeSpan.FromMilliseconds(settings.DelayBetweenRequestsMs), cancellationToken).ConfigureAwait(false);
            }

            all.AddRange(await CheckChunkAsync(chunks[i], cancellationToken).ConfigureAwait(false));
        }

        return all;
    }

    private async Task<IReadOnlyList<LookupResult>> CheckChunkAsync(string[] chunk, CancellationToken cancellationToken)
    {
        var settings = _settings.Current;
        var maxRetries = Math.Max(0, settings.MaxRetriesOnRateLimit);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, settings.RequestTimeoutSeconds)));

                var payload = new UsernameLookupRequestDto { Usernames = chunk, ExcludeBannedUsers = false };
                using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
                {
                    Content = JsonContent.Create(payload)
                };

                using var response = await _http.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
                var code = (int)response.StatusCode;

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    if (attempt < maxRetries)
                    {
                        var wait = GetRetryDelay(response, attempt);
                        _log.Log(Core.LogSeverity.Warning, "Roblox",
                            $"HTTP 429: aguardando {wait.TotalSeconds:0.#}s antes de tentar novamente ({attempt + 1}/{maxRetries}).");
                        await _delay(wait, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    _log.Log(Core.LogSeverity.Warning, "Roblox", "HTTP 429: limite de requisições persistiu após as tentativas.");
                    return Fail(chunk, LookupStatus.Inconclusive, LookupFailureReason.RateLimited, 429,
                        "Limite de requisições do Roblox (HTTP 429). Tente novamente mais tarde ou aumente o intervalo.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    var detail = code switch
                    {
                        400 => "HTTP 400: requisição recusada (por exemplo, usernames demais no lote). Reduza o tamanho do lote.",
                        401 or 403 => $"HTTP {code}: o endpoint recusou a consulta sem autenticação. O aplicativo não usa login nem cookies.",
                        >= 500 => $"HTTP {code}: erro no servidor do Roblox.",
                        _ => $"HTTP {code}: resposta inesperada."
                    };

                    _log.Log(Core.LogSeverity.Warning, "Roblox", detail);
                    return Fail(chunk, LookupStatus.Inconclusive, LookupFailureReason.HttpError, code, detail);
                }

                var body = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
                return ParseResponse(chunk, body, code);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                _log.Log(Core.LogSeverity.Warning, "Roblox", "Tempo limite da requisição excedido.");
                return Fail(chunk, LookupStatus.Inconclusive, LookupFailureReason.Timeout, null,
                    "Tempo limite excedido: o Roblox não respondeu a tempo.");
            }
            catch (HttpRequestException ex)
            {
                _log.Log(Core.LogSeverity.Error, "Roblox", "Falha de conexão.", ex.Message);
                return Fail(chunk, LookupStatus.ConnectionError, LookupFailureReason.Network, null,
                    "Falha de conexão com o Roblox. Verifique sua internet.");
            }
        }
    }

    private IReadOnlyList<LookupResult> ParseResponse(string[] chunk, string body, int httpCode)
    {
        UsernameLookupResponseDto? dto;

        try
        {
            dto = JsonSerializer.Deserialize<UsernameLookupResponseDto>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            _log.Log(Core.LogSeverity.Error, "Roblox", "Resposta JSON inválida.", ex.Message);
            return Fail(chunk, LookupStatus.Inconclusive, LookupFailureReason.InvalidResponse, httpCode,
                "A resposta do Roblox não pôde ser interpretada (JSON inválido).");
        }

        if (dto?.Data is null)
        {
            _log.Log(Core.LogSeverity.Error, "Roblox", "Resposta sem o campo 'data'.");
            return Fail(chunk, LookupStatus.Inconclusive, LookupFailureReason.InvalidResponse, httpCode,
                "A resposta do Roblox não contém o campo esperado ('data').");
        }

        var map = new Dictionary<string, UsernameLookupItemDto>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in dto.Data)
        {
            var key = item?.RequestedUsername;
            if (string.IsNullOrWhiteSpace(key))
            {
                key = item?.Name;
            }

            if (item is null || item.Id <= 0 || string.IsNullOrWhiteSpace(key))
            {
                _log.Log(Core.LogSeverity.Error, "Roblox", "Item inválido na resposta.");
                return Fail(chunk, LookupStatus.Inconclusive, LookupFailureReason.InvalidResponse, httpCode,
                    "A resposta do Roblox contém itens inválidos e não pôde ser confiada.");
            }

            map.TryAdd(key, item);
        }

        var now = _clock();
        var results = new List<LookupResult>(chunk.Length);

        foreach (var name in chunk)
        {
            if (map.TryGetValue(name, out var found))
            {
                results.Add(new LookupResult(
                    name, LookupStatus.AccountFound, LookupFailureReason.None,
                    $"Conta pública encontrada: {found.Name} (ID {found.Id}).",
                    now, found.Id, found.Name, httpCode));
            }
            else
            {
                results.Add(new LookupResult(
                    name, LookupStatus.NoPublicMatch, LookupFailureReason.None,
                    "Nenhuma conta na busca pública. Disponibilidade de registro NÃO confirmada.",
                    now, HttpStatusCode: httpCode));
            }
        }

        _log.Log(Core.LogSeverity.Information, "Roblox",
            $"Lote concluído: {results.Count(r => r.Status == LookupStatus.AccountFound)} encontradas de {chunk.Length}.");

        return results;
    }

    private TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        TimeSpan? wait = null;
        var retryAfter = response.Headers.RetryAfter;

        if (retryAfter?.Delta is { } delta)
        {
            wait = delta;
        }
        else if (retryAfter?.Date is { } date)
        {
            wait = date - _clock();
        }

        wait ??= TimeSpan.FromSeconds(Math.Pow(2, attempt));

        if (wait < TimeSpan.FromSeconds(1))
        {
            wait = TimeSpan.FromSeconds(1);
        }

        return wait > MaxRetryWait ? MaxRetryWait : wait.Value;
    }

    private IReadOnlyList<LookupResult> Fail(
        string[] chunk, LookupStatus status, LookupFailureReason reason, int? httpCode, string details)
    {
        var now = _clock();
        return chunk
            .Select(n => new LookupResult(n, status, reason, details, now, HttpStatusCode: httpCode))
            .ToList();
    }
}

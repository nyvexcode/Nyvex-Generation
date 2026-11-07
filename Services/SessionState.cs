using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using NyvexGeneration.Core;
using NyvexGeneration.Models;
using NyvexGeneration.ViewModels;

namespace NyvexGeneration.Services;

/// <summary>Estado compartilhado da sessão: tabela de resultados, contadores e progresso.</summary>
public sealed partial class SessionState : ObservableObject
{
    private readonly Dictionary<string, ResultRow> _index = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _favorites = new(StringComparer.OrdinalIgnoreCase);
    private Stopwatch? _runWatch;

    public ObservableCollection<ResultRow> Results { get; } = new();

    [ObservableProperty]
    private int _totalGenerated;

    [ObservableProperty]
    private int _accountsFound;

    [ObservableProperty]
    private int _noPublicMatch;

    [ObservableProperty]
    private int _checksCompleted;

    [ObservableProperty]
    private int _errors;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    private int _progressTotal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    private int _progressDone;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private TimeSpan _elapsed;

    [ObservableProperty]
    private ConnectionState _connection = ConnectionState.Unknown;

    public double ProgressPercent => ProgressTotal == 0 ? 0 : 100.0 * ProgressDone / ProgressTotal;

    public ResultRow? Find(string username) => _index.TryGetValue(username, out var row) ? row : null;

    public ResultRow GetOrAdd(string username)
    {
        if (_index.TryGetValue(username, out var existing))
        {
            return existing;
        }

        var row = new ResultRow(username) { IsFavorite = _favorites.Contains(username) };
        _index[username] = row;
        Results.Add(row);
        return row;
    }

    public void MarkChecking(ResultRow row)
    {
        Adjust(row.Status, -1);
        row.Status = LookupStatus.Checking;
        row.Details = "Consulta em andamento…";
    }

    public void ResetPending(ResultRow row, string details)
    {
        Adjust(row.Status, -1);
        row.Status = LookupStatus.NotChecked;
        row.Details = details;
    }

    /// <summary>Aplica um resultado de consulta feita nesta sessão.</summary>
    public void Apply(LookupResult result)
    {
        ApplyInternal(result);

        if (result.Status is LookupStatus.AccountFound or LookupStatus.NoPublicMatch)
        {
            ChecksCompleted++;
        }
    }

    /// <summary>Carrega um resultado do histórico, sem contá-lo como consulta da sessão.</summary>
    public void ApplyLoaded(LookupResult result) => ApplyInternal(result);

    private void ApplyInternal(LookupResult result)
    {
        var row = GetOrAdd(result.Username);
        Adjust(row.Status, -1);
        row.Update(result);
        Adjust(result.Status, +1);
    }

    public void SetFavorites(IEnumerable<string> usernames)
    {
        _favorites.Clear();
        foreach (var name in usernames)
        {
            _favorites.Add(name);
        }

        foreach (var row in Results)
        {
            row.IsFavorite = _favorites.Contains(row.Username);
        }

        foreach (var name in _favorites)
        {
            GetOrAdd(name);
        }
    }

    public void SetFavorite(ResultRow row, bool isFavorite)
    {
        row.IsFavorite = isFavorite;
        if (isFavorite)
        {
            _favorites.Add(row.Username);
        }
        else
        {
            _favorites.Remove(row.Username);
        }
    }

    public void ClearFavorites()
    {
        _favorites.Clear();
        foreach (var row in Results)
        {
            row.IsFavorite = false;
        }
    }

    public void ClearResults()
    {
        Results.Clear();
        _index.Clear();
        AccountsFound = 0;
        NoPublicMatch = 0;
        Errors = 0;
    }

    public void RegisterGenerated(int count) => TotalGenerated += Math.Max(0, count);

    public void BeginRun(int total)
    {
        ProgressTotal = total;
        ProgressDone = 0;
        Elapsed = TimeSpan.Zero;
        _runWatch = Stopwatch.StartNew();
        IsRunning = true;
    }

    public void AdvanceProgress(int count) => ProgressDone = Math.Min(ProgressTotal, ProgressDone + Math.Max(0, count));

    public void Tick()
    {
        if (_runWatch is not null)
        {
            Elapsed = _runWatch.Elapsed;
        }
    }

    public void EndRun()
    {
        Tick();
        _runWatch?.Stop();
        IsRunning = false;
    }

    private void Adjust(LookupStatus status, int delta)
    {
        switch (status)
        {
            case LookupStatus.AccountFound:
                AccountsFound += delta;
                break;
            case LookupStatus.NoPublicMatch:
                NoPublicMatch += delta;
                break;
            case LookupStatus.Inconclusive:
            case LookupStatus.ConnectionError:
                Errors += delta;
                break;
        }
    }
}

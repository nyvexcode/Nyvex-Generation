using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using NyvexGeneration.Core;
using NyvexGeneration.Data;
using NyvexGeneration.Helpers;
using NyvexGeneration.Services;

namespace NyvexGeneration.ViewModels;

public sealed partial class StatCard : ObservableObject
{
    public StatCard(string title, string glyph)
    {
        Title = title;
        Glyph = glyph;
    }

    public string Title { get; }

    public string Glyph { get; }

    [ObservableProperty]
    private string _value = "0";
}

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly SessionState _state;
    private readonly ICheckCoordinator _coordinator;
    private readonly IStatsRepository _stats;
    private readonly ILogService _log;
    private readonly StatCard _generated = new("Usernames gerados", "");
    private readonly StatCard _found = new("Contas encontradas", "");
    private readonly StatCard _noMatch = new("Sem correspondência", "");
    private readonly StatCard _completed = new("Consultas concluídas", "");
    private readonly StatCard _errors = new("Erros", "");

    public DashboardViewModel(SessionState state, ICheckCoordinator coordinator, IStatsRepository stats, ILogService log)
    {
        _state = state;
        _coordinator = coordinator;
        _stats = stats;
        _log = log;

        Cards = new ObservableCollection<StatCard> { _generated, _found, _noMatch, _completed, _errors };
        _state.PropertyChanged += OnStateChanged;
        RefreshCards();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            if (_state.IsRunning)
            {
                _state.Tick();
            }
        };
        timer.Start();

        WeakReferenceMessenger.Default.Register<DashboardViewModel, DataChangedMessage>(this, static (r, m) =>
        {
            if (m.Kind == DataKind.Stats)
            {
                _ = r.RefreshLifetimeAsync();
            }
        });
    }

    public SessionState State => _state;

    public ObservableCollection<StatCard> Cards { get; }

    [ObservableProperty]
    private string _lifetimeText = string.Empty;

    public string ElapsedText => _state.Elapsed.ToString(@"hh\:mm\:ss");

    public string ProgressText => _state.ProgressTotal == 0
        ? "Nenhuma consulta em andamento"
        : $"{_state.ProgressDone} / {_state.ProgressTotal}";

    public string ConnectionText => StatusFormatter.ConnectionText(_state.Connection);

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _coordinator.Cancel();

    private bool CanCancel() => _state.IsRunning;

    public async Task RefreshLifetimeAsync()
    {
        try
        {
            var all = await _stats.GetAllAsync(CancellationToken.None);
            long Get(string key) => all.TryGetValue(key, out var v) ? v : 0;

            LifetimeText =
                $"Histórico acumulado: {Get(StatKeys.Generated)} gerados · " +
                $"{Get(StatKeys.AccountsFound)} encontradas · " +
                $"{Get(StatKeys.NoPublicMatch)} sem correspondência · " +
                $"{Get(StatKeys.ChecksCompleted)} consultas · {Get(StatKeys.Errors)} erros";
        }
        catch (Exception ex)
        {
            _log.Log(LogSeverity.Error, "Database", "Falha ao ler estatísticas.", ex.Message);
        }
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SessionState.Elapsed):
                OnPropertyChanged(nameof(ElapsedText));
                break;
            case nameof(SessionState.ProgressDone):
            case nameof(SessionState.ProgressTotal):
                OnPropertyChanged(nameof(ProgressText));
                break;
            case nameof(SessionState.Connection):
                OnPropertyChanged(nameof(ConnectionText));
                break;
            case nameof(SessionState.IsRunning):
                CancelCommand.NotifyCanExecuteChanged();
                if (!_state.IsRunning)
                {
                    _ = RefreshLifetimeAsync();
                }

                break;
            case nameof(SessionState.TotalGenerated):
            case nameof(SessionState.AccountsFound):
            case nameof(SessionState.NoPublicMatch):
            case nameof(SessionState.ChecksCompleted):
            case nameof(SessionState.Errors):
                RefreshCards();
                break;
        }
    }

    private void RefreshCards()
    {
        _generated.Value = _state.TotalGenerated.ToString();
        _found.Value = _state.AccountsFound.ToString();
        _noMatch.Value = _state.NoPublicMatch.ToString();
        _completed.Value = _state.ChecksCompleted.ToString();
        _errors.Value = _state.Errors.ToString();
    }
}

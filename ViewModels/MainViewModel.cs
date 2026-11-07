using CommunityToolkit.Mvvm.ComponentModel;
using NyvexGeneration.Core;
using NyvexGeneration.Data;
using NyvexGeneration.Infrastructure;
using NyvexGeneration.Services;

namespace NyvexGeneration.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ISettingsRepository _settingsRepository;
    private readonly ILookupHistoryRepository _lookupHistory;
    private readonly IFavoritesRepository _favorites;
    private readonly SettingsState _settings;
    private readonly ThemeService _themes;
    private readonly ILogService _log;

    public MainViewModel(
        DashboardViewModel dashboard,
        GeneratorViewModel generator,
        ResultsViewModel results,
        LogsViewModel logs,
        SettingsViewModel settingsPage,
        SessionState state,
        ISettingsRepository settingsRepository,
        ILookupHistoryRepository lookupHistory,
        IFavoritesRepository favorites,
        SettingsState settings,
        ThemeService themes,
        ILogService log)
    {
        Dashboard = dashboard;
        Generator = generator;
        Results = results;
        Logs = logs;
        SettingsPage = settingsPage;
        State = state;

        _settingsRepository = settingsRepository;
        _lookupHistory = lookupHistory;
        _favorites = favorites;
        _settings = settings;
        _themes = themes;
        _log = log;

        NavItems = new[]
        {
            new NavItem("Dashboard", "", dashboard),
            new NavItem("Gerador", "", generator),
            new NavItem("Resultados", "", results),
            new NavItem("Logs", "", logs),
            new NavItem("Configurações", "", settingsPage)
        };

        _selectedNav = NavItems[0];
        _currentPage = NavItems[0].Page;
    }

    public string AppName => "NYVEX";

    public string AppSubtitle => "GENERATION";

    public string Version { get; } = "v" + (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "3.0.0");

    public DashboardViewModel Dashboard { get; }

    public GeneratorViewModel Generator { get; }

    public ResultsViewModel Results { get; }

    public LogsViewModel Logs { get; }

    public SettingsViewModel SettingsPage { get; }

    public SessionState State { get; }

    public IReadOnlyList<NavItem> NavItems { get; }

    [ObservableProperty]
    private NavItem _selectedNav;

    [ObservableProperty]
    private object _currentPage;

    partial void OnSelectedNavChanged(NavItem value)
    {
        if (value is not null)
        {
            CurrentPage = value.Page;
        }
    }

    /// <summary>Carrega configurações, favoritos e histórico. Falhas não impedem a abertura do app.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            var loaded = await _settingsRepository.LoadAsync(CancellationToken.None);
            _settings.Update(loaded);
            _themes.Apply(_settings.Current.Theme);
            SettingsPage.LoadFrom(_settings.Current);

            var history = await _lookupHistory.GetRecentAsync(5000, CancellationToken.None);
            foreach (var result in history)
            {
                State.ApplyLoaded(result);
            }

            State.SetFavorites(await _favorites.GetAllAsync(CancellationToken.None));

            await Generator.InitializeAsync();
            await Dashboard.RefreshLifetimeAsync();

            _log.Log(LogSeverity.Information, "App", $"Nyvex Generation iniciado. {history.Count} resultado(s) carregado(s) do histórico.");
        }
        catch (Exception ex)
        {
            _log.Log(LogSeverity.Error, "App", "Falha ao carregar dados salvos.", ex.Message);
        }
    }
}

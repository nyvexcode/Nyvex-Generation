using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using NyvexGeneration.Core;
using NyvexGeneration.Data;
using NyvexGeneration.Infrastructure;
using NyvexGeneration.Models;
using NyvexGeneration.Services;

namespace NyvexGeneration.ViewModels;

public sealed record DelimiterOption(string Label, char Value);

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsState _settings;
    private readonly ISettingsRepository _repository;
    private readonly ThemeService _themes;
    private readonly IDialogService _dialogs;
    private readonly IBackupService _backup;
    private readonly IGenerationHistoryRepository _generationHistory;
    private readonly ILookupHistoryRepository _lookupHistory;
    private readonly IFavoritesRepository _favorites;
    private readonly IStatsRepository _stats;
    private readonly SessionState _state;
    private readonly AppPaths _paths;
    private readonly ILogService _log;
    private bool _loading;

    public SettingsViewModel(
        SettingsState settings,
        ISettingsRepository repository,
        ThemeService themes,
        IDialogService dialogs,
        IBackupService backup,
        IGenerationHistoryRepository generationHistory,
        ILookupHistoryRepository lookupHistory,
        IFavoritesRepository favorites,
        IStatsRepository stats,
        SessionState state,
        AppPaths paths,
        ILogService log)
    {
        _settings = settings;
        _repository = repository;
        _themes = themes;
        _dialogs = dialogs;
        _backup = backup;
        _generationHistory = generationHistory;
        _lookupHistory = lookupHistory;
        _favorites = favorites;
        _stats = stats;
        _state = state;
        _paths = paths;
        _log = log;

        Themes = ThemeService.Themes;
        Delimiters = new[]
        {
            new DelimiterOption("Ponto e vírgula (;)", ';'),
            new DelimiterOption("Vírgula (,)", ',')
        };
        _selectedTheme = Themes[0];
        _selectedDelimiter = Delimiters[0];

        LoadFrom(_settings.Current);
    }

    public IReadOnlyList<ThemeOption> Themes { get; }

    public IReadOnlyList<int> LengthOptions { get; } = new[] { 3, 4, 5 };

    public IReadOnlyList<DelimiterOption> Delimiters { get; }

    public string DataDirectory => _paths.RootDirectory;

    public string DatabasePath => _paths.DatabaseFile;

    [ObservableProperty]
    private ThemeOption _selectedTheme;

    [ObservableProperty]
    private int _defaultLength = 4;

    [ObservableProperty]
    private string _defaultQuantityText = "100";

    [ObservableProperty]
    private bool _autoCheck = true;

    [ObservableProperty]
    private bool _avoidPreviouslyGenerated = true;

    [ObservableProperty]
    private string _delayText = "1500";

    [ObservableProperty]
    private string _batchSizeText = "50";

    [ObservableProperty]
    private string _timeoutText = "15";

    [ObservableProperty]
    private string _retriesText = "3";

    [ObservableProperty]
    private DelimiterOption _selectedDelimiter;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        if (!_loading)
        {
            _themes.Apply(value.Key);
        }
    }

    public void LoadFrom(AppSettings s)
    {
        _loading = true;
        try
        {
            SelectedTheme = Themes.FirstOrDefault(t => t.Key == s.Theme) ?? Themes[0];
            DefaultLength = s.DefaultLength;
            DefaultQuantityText = s.DefaultQuantity.ToString();
            AutoCheck = s.AutoCheckAfterGeneration;
            AvoidPreviouslyGenerated = s.AvoidPreviouslyGenerated;
            DelayText = s.DelayBetweenRequestsMs.ToString();
            BatchSizeText = s.BatchSize.ToString();
            TimeoutText = s.RequestTimeoutSeconds.ToString();
            RetriesText = s.MaxRetriesOnRateLimit.ToString();
            SelectedDelimiter = Delimiters.FirstOrDefault(d => d.Value == s.CsvDelimiter) ?? Delimiters[0];
        }
        finally
        {
            _loading = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var current = _settings.Current.Clone();
        current.Theme = SelectedTheme.Key;
        current.DefaultLength = DefaultLength;
        current.DefaultQuantity = ParseInt(DefaultQuantityText, current.DefaultQuantity);
        current.AutoCheckAfterGeneration = AutoCheck;
        current.AvoidPreviouslyGenerated = AvoidPreviouslyGenerated;
        current.DelayBetweenRequestsMs = ParseInt(DelayText, current.DelayBetweenRequestsMs);
        current.BatchSize = ParseInt(BatchSizeText, current.BatchSize);
        current.RequestTimeoutSeconds = ParseInt(TimeoutText, current.RequestTimeoutSeconds);
        current.MaxRetriesOnRateLimit = ParseInt(RetriesText, current.MaxRetriesOnRateLimit);
        current.CsvDelimiter = SelectedDelimiter.Value;
        current.Normalize();

        try
        {
            await _repository.SaveAsync(current, CancellationToken.None);
            _settings.Update(current);
            _themes.Apply(current.Theme);
            LoadFrom(_settings.Current);
            _log.Log(LogSeverity.Information, "Settings", "Configurações salvas.");
            StatusMessage = "Configurações salvas. Valores fora dos limites foram ajustados automaticamente.";
        }
        catch (Exception ex)
        {
            _log.Log(LogSeverity.Error, "Settings", "Falha ao salvar configurações.", ex.Message);
            StatusMessage = "Falha ao salvar configurações. Veja a aba Logs.";
        }
    }

    [RelayCommand]
    private void RestoreDefaults()
    {
        LoadFrom(new AppSettings());
        _themes.Apply(SelectedTheme.Key);
        StatusMessage = "Valores padrão carregados. Clique em Salvar para aplicar.";
    }

    [RelayCommand]
    private Task ClearGenerationHistoryAsync() => ClearAsync(
        "histórico de geração",
        () => _generationHistory.ClearAsync(CancellationToken.None),
        DataKind.GenerationHistory);

    [RelayCommand]
    private Task ClearLookupHistoryAsync() => ClearAsync(
        "histórico de consultas",
        async () =>
        {
            await _lookupHistory.ClearAsync(CancellationToken.None);
            _state.ClearResults();
            _state.SetFavorites(await _favorites.GetAllAsync(CancellationToken.None));
        },
        DataKind.LookupHistory);

    [RelayCommand]
    private Task ClearFavoritesAsync() => ClearAsync(
        "favoritos",
        async () =>
        {
            await _favorites.ClearAsync(CancellationToken.None);
            _state.ClearFavorites();
        },
        DataKind.Favorites);

    [RelayCommand]
    private Task ClearStatsAsync() => ClearAsync(
        "estatísticas acumuladas",
        () => _stats.ClearAsync(CancellationToken.None),
        DataKind.Stats);

    [RelayCommand]
    private async Task BackupAsync()
    {
        var path = _dialogs.PickSaveFile(
            "Salvar backup do banco",
            $"nyvex-backup-{DateTime.Now:yyyyMMdd-HHmmss}.db",
            "Banco SQLite (*.db)|*.db");

        if (path is null)
        {
            return;
        }

        try
        {
            await _backup.BackupAsync(path, CancellationToken.None);
            _log.Log(LogSeverity.Information, "Backup", "Backup do banco criado.");
            StatusMessage = $"Backup salvo em {path}";
        }
        catch (Exception ex)
        {
            _log.Log(LogSeverity.Error, "Backup", "Falha ao criar backup.", ex.Message);
            StatusMessage = "Falha ao criar o backup. Veja a aba Logs.";
        }
    }

    [RelayCommand]
    private void OpenDataFolder() => _dialogs.OpenFolder(_paths.RootDirectory);

    private async Task ClearAsync(string what, Func<Task> action, DataKind kind)
    {
        if (_state.IsRunning)
        {
            StatusMessage = "Cancele a consulta em andamento antes de limpar dados.";
            return;
        }

        if (!_dialogs.Confirm("Limpar dados", $"Apagar permanentemente: {what}?"))
        {
            return;
        }

        try
        {
            await action();
            WeakReferenceMessenger.Default.Send(new DataChangedMessage(kind));
            _log.Log(LogSeverity.Information, "Settings", $"Dados limpos: {what}.");
            StatusMessage = $"Limpo: {what}.";
        }
        catch (Exception ex)
        {
            _log.Log(LogSeverity.Error, "Settings", $"Falha ao limpar {what}.", ex.Message);
            StatusMessage = "Falha ao limpar os dados. Veja a aba Logs.";
        }
    }

    private static int ParseInt(string? text, int fallback) =>
        int.TryParse(text?.Trim(), out var value) ? value : fallback;
}

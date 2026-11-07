using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NyvexGeneration.Core;
using NyvexGeneration.Data;
using NyvexGeneration.Infrastructure;
using NyvexGeneration.Services;

namespace NyvexGeneration.ViewModels;

public enum ResultFilter
{
    All,
    Found,
    NoMatch,
    Inconclusive,
    Errors,
    Pending,
    Favorites
}

public sealed record FilterOption(string Label, ResultFilter Filter);

public sealed partial class ResultsViewModel : ObservableObject
{
    private readonly SessionState _state;
    private readonly ICheckCoordinator _coordinator;
    private readonly IExportService _export;
    private readonly IFavoritesRepository _favorites;
    private readonly IDialogService _dialogs;
    private readonly IUsernameValidator _validator;
    private readonly SettingsState _settings;
    private readonly ILogService _log;
    private readonly AppPaths _paths;

    public ResultsViewModel(
        SessionState state,
        ICheckCoordinator coordinator,
        IExportService export,
        IFavoritesRepository favorites,
        IDialogService dialogs,
        IUsernameValidator validator,
        SettingsState settings,
        ILogService log,
        AppPaths paths)
    {
        _state = state;
        _coordinator = coordinator;
        _export = export;
        _favorites = favorites;
        _dialogs = dialogs;
        _validator = validator;
        _settings = settings;
        _log = log;
        _paths = paths;

        Filters = new[]
        {
            new FilterOption("Todos", ResultFilter.All),
            new FilterOption("Encontradas", ResultFilter.Found),
            new FilterOption("Sem correspondência", ResultFilter.NoMatch),
            new FilterOption("Inconclusivas", ResultFilter.Inconclusive),
            new FilterOption("Erros", ResultFilter.Errors),
            new FilterOption("Pendentes", ResultFilter.Pending),
            new FilterOption("Favoritos", ResultFilter.Favorites)
        };
        _selectedFilter = Filters[0];

        View = CollectionViewSource.GetDefaultView(_state.Results);
        View.Filter = FilterRow;

        if (View is ICollectionViewLiveShaping live && live.CanChangeLiveFiltering)
        {
            live.LiveFilteringProperties.Add(nameof(ResultRow.Status));
            live.LiveFilteringProperties.Add(nameof(ResultRow.IsFavorite));
            live.IsLiveFiltering = true;
        }

        ((INotifyCollectionChanged)View).CollectionChanged += (_, _) => UpdateVisibleCount();
        _state.Results.CollectionChanged += OnResultsChanged;
        _state.PropertyChanged += OnStateChanged;
        UpdateVisibleCount();
    }

    public SessionState State => _state;

    public ICollectionView View { get; }

    public IReadOnlyList<FilterOption> Filters { get; }

    [ObservableProperty]
    private FilterOption _selectedFilter;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _singleUsername = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private int _visibleCount;

    [ObservableProperty]
    private int _selectedCount;

    public bool IsChecking => _state.IsRunning;

    partial void OnSearchTextChanged(string value) => View.Refresh();

    partial void OnSelectedFilterChanged(FilterOption value) => View.Refresh();

    // ---------- Consultas ----------

    [RelayCommand]
    private async Task CheckSingleAsync()
    {
        var name = SingleUsername?.Trim().ToLowerInvariant() ?? string.Empty;

        if (!_validator.IsValid(name, out var error))
        {
            StatusMessage = error ?? "Username inválido.";
            return;
        }

        StatusMessage = $"Consultando \"{name}\"…";
        var count = await _coordinator.RunAsync(new[] { name }, force: true);
        StatusMessage = count > 0 ? $"Consulta de \"{name}\" concluída." : "Não foi possível iniciar a consulta.";
        SingleUsername = string.Empty;
    }

    [RelayCommand]
    private async Task CheckPendingAsync()
    {
        var pending = _state.Results
            .Where(r => r.Status == LookupStatus.NotChecked)
            .Select(r => r.Username)
            .ToList();

        if (pending.Count == 0)
        {
            StatusMessage = "Não há usernames pendentes.";
            return;
        }

        StatusMessage = $"Consultando {pending.Count} pendente(s)…";
        var count = await _coordinator.RunAsync(pending, force: false);
        StatusMessage = $"{count} consulta(s) entregue(s).";
    }

    [RelayCommand]
    private async Task RecheckSelectedAsync()
    {
        var names = SelectedRows().Select(r => r.Username).ToList();
        if (names.Count == 0)
        {
            StatusMessage = "Selecione ao menos um username.";
            return;
        }

        StatusMessage = $"Reconsultando {names.Count} username(s)…";
        var count = await _coordinator.RunAsync(names, force: true);
        StatusMessage = $"{count} consulta(s) entregue(s).";
    }

    [RelayCommand]
    private void CancelCheck() => _coordinator.Cancel();

    // ---------- Seleção ----------

    [RelayCommand]
    private void SelectAllVisible()
    {
        foreach (var row in VisibleRows())
        {
            row.IsSelected = true;
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var row in _state.Results.Where(r => r.IsSelected).ToList())
        {
            row.IsSelected = false;
        }
    }

    [RelayCommand]
    private void CopySelected()
    {
        var rows = SelectedRows();
        if (rows.Count == 0)
        {
            StatusMessage = "Selecione ao menos um username para copiar.";
            return;
        }

        var ok = _dialogs.CopyToClipboard(string.Join(Environment.NewLine, rows.Select(r => r.Username)));
        StatusMessage = ok ? $"{rows.Count} username(s) copiado(s)." : "Não foi possível acessar a área de transferência.";
    }

    [RelayCommand]
    private async Task CopyUsernameAsync(ResultRow? row)
    {
        if (row is null)
        {
            return;
        }

        var ok = _dialogs.CopyToClipboard(row.Username);
        StatusMessage = ok ? $"\"{row.Username}\" copiado." : "Não foi possível acessar a área de transferência.";
        await Task.CompletedTask;
    }

    // ---------- Favoritos ----------

    [RelayCommand]
    private async Task ToggleFavoriteAsync(ResultRow? row)
    {
        if (row is null)
        {
            return;
        }

        var newValue = !row.IsFavorite;
        _state.SetFavorite(row, newValue);

        try
        {
            if (newValue)
            {
                await _favorites.AddAsync(row.Username, CancellationToken.None);
            }
            else
            {
                await _favorites.RemoveAsync(row.Username, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _state.SetFavorite(row, !newValue);
            _log.Log(LogSeverity.Error, "Database", "Falha ao salvar favorito.", ex.Message);
            StatusMessage = "Falha ao salvar favorito. Veja a aba Logs.";
        }
    }

    [RelayCommand]
    private async Task FavoriteSelectedAsync()
    {
        var rows = SelectedRows().Where(r => !r.IsFavorite).ToList();
        if (rows.Count == 0)
        {
            StatusMessage = "Nenhum username selecionado para favoritar.";
            return;
        }

        try
        {
            foreach (var row in rows)
            {
                await _favorites.AddAsync(row.Username, CancellationToken.None);
                _state.SetFavorite(row, true);
            }

            StatusMessage = $"{rows.Count} username(s) favoritado(s).";
        }
        catch (Exception ex)
        {
            _log.Log(LogSeverity.Error, "Database", "Falha ao salvar favoritos.", ex.Message);
            StatusMessage = "Falha ao salvar favoritos. Veja a aba Logs.";
        }
    }

    // ---------- Exportação ----------

    [RelayCommand]
    private Task ExportTxtAsync() => ExportAsync("txt");

    [RelayCommand]
    private Task ExportCsvAsync() => ExportAsync("csv");

    private async Task ExportAsync(string format)
    {
        var rows = SelectedRows();
        var scope = "selecionados";
        if (rows.Count == 0)
        {
            rows = VisibleRows();
            scope = "visíveis";
        }

        if (rows.Count == 0)
        {
            StatusMessage = "Não há resultados para exportar.";
            return;
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var filter = format == "csv" ? "CSV (*.csv)|*.csv" : "Texto (*.txt)|*.txt";
        var path = _dialogs.PickSaveFile("Exportar resultados", $"nyvex-{stamp}.{format}", filter);
        if (path is null)
        {
            return;
        }

        try
        {
            var results = rows.Select(r => r.ToLookupResult()).ToList();
            if (format == "csv")
            {
                await _export.ExportCsvAsync(results, path, CancellationToken.None, _settings.Current.CsvDelimiter);
            }
            else
            {
                await _export.ExportTxtAsync(results, path, CancellationToken.None);
            }

            _log.Log(LogSeverity.Information, "Export", $"{results.Count} resultado(s) exportado(s) ({format}).");
            StatusMessage = $"{results.Count} resultado(s) {scope} exportado(s) para {path}";
        }
        catch (Exception ex)
        {
            _log.Log(LogSeverity.Error, "Export", "Falha na exportação.", ex.Message);
            StatusMessage = "Falha ao exportar. Veja a aba Logs.";
        }
    }

    // ---------- Limpeza ----------

    [RelayCommand]
    private void ClearResults()
    {
        if (_coordinator.IsRunning)
        {
            StatusMessage = "Cancele a consulta antes de limpar a tabela.";
            return;
        }

        if (!_dialogs.Confirm("Limpar tabela", "Remover todos os resultados da tabela? O histórico salvo no banco não é apagado."))
        {
            return;
        }

        _state.ClearResults();
        StatusMessage = "Tabela limpa.";
    }

    // ---------- Internos ----------

    private bool FilterRow(object item)
    {
        if (item is not ResultRow row)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SearchText) &&
            !row.Username.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return SelectedFilter.Filter switch
        {
            ResultFilter.Found => row.Status == LookupStatus.AccountFound,
            ResultFilter.NoMatch => row.Status == LookupStatus.NoPublicMatch,
            ResultFilter.Inconclusive => row.Status == LookupStatus.Inconclusive,
            ResultFilter.Errors => row.Status == LookupStatus.ConnectionError,
            ResultFilter.Pending => row.Status is LookupStatus.NotChecked or LookupStatus.Checking,
            ResultFilter.Favorites => row.IsFavorite,
            _ => true
        };
    }

    private List<ResultRow> VisibleRows() => View.Cast<ResultRow>().ToList();

    private List<ResultRow> SelectedRows() => _state.Results.Where(r => r.IsSelected).ToList();

    private void UpdateVisibleCount() => VisibleCount = View.Cast<object>().Count();

    private void OnResultsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            SelectedCount = 0;
            return;
        }

        if (e.NewItems is not null)
        {
            foreach (ResultRow row in e.NewItems)
            {
                row.PropertyChanged += OnRowChanged;
            }
        }
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResultRow.IsSelected) && sender is ResultRow row)
        {
            SelectedCount += row.IsSelected ? 1 : -1;
        }
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionState.IsRunning))
        {
            OnPropertyChanged(nameof(IsChecking));
        }
    }
}

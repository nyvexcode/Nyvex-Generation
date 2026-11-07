using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using NyvexGeneration.Core;
using NyvexGeneration.Data;
using NyvexGeneration.Models;
using NyvexGeneration.Services;

namespace NyvexGeneration.ViewModels;

public sealed record CharsetOption(string Label, CharacterSets Value);

public sealed partial class GeneratorViewModel : ObservableObject
{
    private const int UiMaxQuantity = 5000;

    private readonly IUsernameGenerator _generator;
    private readonly IGenerationHistoryRepository _history;
    private readonly IStatsRepository _stats;
    private readonly ICheckCoordinator _coordinator;
    private readonly SessionState _state;
    private readonly SettingsState _settings;
    private readonly ILogService _log;

    public GeneratorViewModel(
        IUsernameGenerator generator,
        IGenerationHistoryRepository history,
        IStatsRepository stats,
        ICheckCoordinator coordinator,
        SessionState state,
        SettingsState settings,
        ILogService log)
    {
        _generator = generator;
        _history = history;
        _stats = stats;
        _coordinator = coordinator;
        _state = state;
        _settings = settings;
        _log = log;

        CharsetOptions = new[]
        {
            new CharsetOption("Letras", CharacterSets.Letters),
            new CharsetOption("Números", CharacterSets.Digits),
            new CharsetOption("Alfanumérico", CharacterSets.LettersAndDigits)
        };
        _selectedCharset = CharsetOptions[2];

        ApplyDefaults();
        _settings.Changed += (_, _) => ApplyDefaults();
        _state.PropertyChanged += OnStateChanged;

        WeakReferenceMessenger.Default.Register<GeneratorViewModel, DataChangedMessage>(this, static (r, m) =>
        {
            if (m.Kind == DataKind.GenerationHistory)
            {
                r.History.Clear();
            }
        });
    }

    public IReadOnlyList<int> LengthOptions { get; } = new[] { 3, 4, 5 };

    public IReadOnlyList<CharsetOption> CharsetOptions { get; }

    public ObservableCollection<GenerationHistoryItem> History { get; } = new();

    [ObservableProperty]
    private int _selectedLength = 4;

    [ObservableProperty]
    private CharsetOption _selectedCharset;

    [ObservableProperty]
    private string _quantityText = "100";

    [ObservableProperty]
    private string _prefix = string.Empty;

    [ObservableProperty]
    private string _suffix = string.Empty;

    [ObservableProperty]
    private string _excludedCharacters = string.Empty;

    [ObservableProperty]
    private bool _autoCheck = true;

    [ObservableProperty]
    private string _statusMessage = "Configure as opções e clique em Gerar.";

    public bool IsChecking => _state.IsRunning;

    public async Task InitializeAsync()
    {
        await RefreshHistoryAsync();
    }

    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (_coordinator.IsRunning)
        {
            StatusMessage = "Aguarde a consulta atual terminar ou cancele-a.";
            return;
        }

        if (!int.TryParse(QuantityText?.Trim(), out var quantity) || quantity < 1)
        {
            StatusMessage = "Informe uma quantidade válida (número inteiro maior que zero).";
            return;
        }

        if (quantity > UiMaxQuantity)
        {
            StatusMessage = $"A quantidade máxima por geração é {UiMaxQuantity}.";
            return;
        }

        var options = new GenerationOptions
        {
            Length = SelectedLength,
            CharacterSet = SelectedCharset.Value,
            Quantity = quantity,
            Prefix = Prefix ?? string.Empty,
            Suffix = Suffix ?? string.Empty,
            ExcludedCharacters = ExcludedCharacters ?? string.Empty
        };

        var error = _generator.ValidateOptions(options);
        if (error is not null)
        {
            StatusMessage = error;
            return;
        }

        List<string> generated;

        try
        {
            IReadOnlySet<string>? exclude = null;
            if (_settings.Current.AvoidPreviouslyGenerated)
            {
                exclude = await _history.GetAllUsernamesAsync(CancellationToken.None);
            }

            generated = (await Task.Run(() => _generator.Generate(options, exclude))).ToList();

            if (generated.Count == 0)
            {
                StatusMessage = "Nenhum username novo pôde ser gerado com essas opções (espaço esgotado ou tudo já gerado).";
                return;
            }

            await _history.SaveAsync(generated, DateTimeOffset.Now, CancellationToken.None);
            await _stats.AddAsync(StatKeys.Generated, generated.Count, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.Log(LogSeverity.Error, "Generator", "Falha ao gerar usernames.", ex.Message);
            StatusMessage = "Falha ao gerar usernames. Veja a aba Logs.";
            return;
        }

        foreach (var name in generated)
        {
            _state.GetOrAdd(name);
        }

        _state.RegisterGenerated(generated.Count);
        _log.Log(LogSeverity.Information, "Generator", $"{generated.Count} username(s) gerado(s).");

        StatusMessage = generated.Count < quantity
            ? $"{generated.Count} gerado(s) de {quantity} solicitados (espaço de combinações limitado)."
            : $"{generated.Count} username(s) gerado(s).";

        await RefreshHistoryAsync();
        WeakReferenceMessenger.Default.Send(new DataChangedMessage(DataKind.Stats));

        if (AutoCheck)
        {
            StatusMessage += " Consultando automaticamente…";
            await _coordinator.RunAsync(generated, force: false);
            StatusMessage = $"{generated.Count} gerado(s). Consulta finalizada — veja a aba Resultados.";
        }
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        try
        {
            await _history.ClearAsync(CancellationToken.None);
            History.Clear();
            StatusMessage = "Histórico de geração limpo.";
        }
        catch (Exception ex)
        {
            _log.Log(LogSeverity.Error, "Database", "Falha ao limpar o histórico de geração.", ex.Message);
        }
    }

    [RelayCommand]
    private void CancelCheck() => _coordinator.Cancel();

    private void ApplyDefaults()
    {
        var s = _settings.Current;
        SelectedLength = s.DefaultLength;
        QuantityText = s.DefaultQuantity.ToString();
        AutoCheck = s.AutoCheckAfterGeneration;
    }

    private async Task RefreshHistoryAsync()
    {
        try
        {
            var items = await _history.GetRecentAsync(200, CancellationToken.None);
            History.Clear();
            foreach (var item in items)
            {
                History.Add(item);
            }
        }
        catch (Exception ex)
        {
            _log.Log(LogSeverity.Error, "Database", "Falha ao carregar o histórico de geração.", ex.Message);
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

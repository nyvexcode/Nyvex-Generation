using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NyvexGeneration.Core;
using NyvexGeneration.Infrastructure;
using NyvexGeneration.Models;
using NyvexGeneration.Services;

namespace NyvexGeneration.ViewModels;

public sealed record LogFilterOption(string Label, LogSeverity MinimumSeverity);

public sealed partial class LogsViewModel : ObservableObject
{
    private const int MaxVisibleEntries = 2000;

    private readonly ILogService _log;
    private readonly IDialogService _dialogs;
    private readonly AppPaths _paths;
    private readonly SynchronizationContext? _context;

    public LogsViewModel(ILogService log, IDialogService dialogs, AppPaths paths)
    {
        _log = log;
        _dialogs = dialogs;
        _paths = paths;
        _context = SynchronizationContext.Current;

        Filters = new[]
        {
            new LogFilterOption("Todos", LogSeverity.Debug),
            new LogFilterOption("Avisos e erros", LogSeverity.Warning),
            new LogFilterOption("Apenas erros", LogSeverity.Error)
        };
        _selectedFilter = Filters[0];

        foreach (var entry in _log.GetRecent(MaxVisibleEntries))
        {
            Entries.Insert(0, entry);
        }

        View = CollectionViewSource.GetDefaultView(Entries);
        View.Filter = o => o is LogEntry e && e.Severity >= SelectedFilter.MinimumSeverity;

        _log.EntryAdded += OnEntryAdded;
    }

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public ICollectionView View { get; }

    public IReadOnlyList<LogFilterOption> Filters { get; }

    [ObservableProperty]
    private LogFilterOption _selectedFilter;

    partial void OnSelectedFilterChanged(LogFilterOption value) => View.Refresh();

    [RelayCommand]
    private void Clear()
    {
        _log.Clear();
        Entries.Clear();
    }

    [RelayCommand]
    private void OpenFolder() => _dialogs.OpenFolder(_paths.LogsDirectory);

    [RelayCommand]
    private void CopyVisible()
    {
        var builder = new StringBuilder();
        foreach (var item in View)
        {
            if (item is LogEntry e)
            {
                builder.Append($"{e.Timestamp:yyyy-MM-dd HH:mm:ss} [{e.Severity}] [{e.Category}] {e.Message}");
                if (!string.IsNullOrEmpty(e.TechnicalDetails))
                {
                    builder.Append(" | ").Append(e.TechnicalDetails);
                }

                builder.AppendLine();
            }
        }

        _dialogs.CopyToClipboard(builder.ToString());
    }

    private void OnEntryAdded(object? sender, LogEntry entry)
    {
        if (_context is null)
        {
            AddEntry(entry);
        }
        else
        {
            _context.Post(_ => AddEntry(entry), null);
        }
    }

    private void AddEntry(LogEntry entry)
    {
        Entries.Insert(0, entry);
        if (Entries.Count > MaxVisibleEntries)
        {
            Entries.RemoveAt(Entries.Count - 1);
        }
    }
}

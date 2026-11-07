using CommunityToolkit.Mvvm.ComponentModel;
using NyvexGeneration.Core;
using NyvexGeneration.Helpers;
using NyvexGeneration.Models;

namespace NyvexGeneration.ViewModels;

public sealed partial class ResultRow : ObservableObject
{
    public ResultRow(string username)
    {
        Username = username;
    }

    public string Username { get; }

    public int Length => Username.Length;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private LookupStatus _status = LookupStatus.NotChecked;

    [ObservableProperty]
    private string _details = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CheckedAtText))]
    private DateTimeOffset? _checkedAt;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _isSelected;

    public string StatusText => StatusFormatter.Text(Status);

    public string CheckedAtText => CheckedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") ?? "—";

    public void Update(LookupResult result)
    {
        Status = result.Status;
        Details = result.Details;
        CheckedAt = result.CheckedAt;
    }

    public LookupResult ToLookupResult() => new(
        Username,
        Status,
        LookupFailureReason.None,
        Details,
        CheckedAt ?? DateTimeOffset.Now);
}

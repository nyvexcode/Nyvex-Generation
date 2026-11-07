using NyvexGeneration.Core;
using NyvexGeneration.Helpers;
using NyvexGeneration.Infrastructure;
using NyvexGeneration.Models;
using NyvexGeneration.Services;
using Xunit;

namespace NyvexGeneration.Tests;

public class FoundationTests
{
    [Fact]
    public void AppPaths_CreatesAllDirectories()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.Path, "data");

        var paths = new AppPaths(root);

        Assert.True(Directory.Exists(paths.RootDirectory));
        Assert.True(Directory.Exists(paths.LogsDirectory));
        Assert.True(Directory.Exists(paths.BackupsDirectory));
        Assert.True(Directory.Exists(paths.ExportsDirectory));
        Assert.Equal(Path.Combine(root, "nyvex.db"), paths.DatabaseFile);
    }

    [Fact]
    public void AppPaths_RejectsEmptyRoot()
    {
        Assert.Throws<ArgumentException>(() => new AppPaths("  "));
    }

    [Theory]
    [InlineData(LookupStatus.AccountFound)]
    [InlineData(LookupStatus.NoPublicMatch)]
    [InlineData(LookupStatus.Inconclusive)]
    [InlineData(LookupStatus.ConnectionError)]
    public void LookupResult_NeverConfirmsRegistrationAvailability(LookupStatus status)
    {
        var result = new LookupResult("abcd", status, LookupFailureReason.None, "teste", DateTimeOffset.UtcNow);

        Assert.False(result.RegistrationAvailabilityConfirmed);
        Assert.Equal(4, result.Length);
    }

    [Fact]
    public void StatusTexts_AreDistinctAndNeverClaimAvailability()
    {
        var texts = Enum.GetValues<LookupStatus>().Select(StatusFormatter.Text).ToList();

        Assert.Equal(texts.Count, texts.Distinct().Count());
        Assert.DoesNotContain(texts, t => t.Contains("livre", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(texts, t => t.Contains("disponível", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ConnectionTexts_CoverAllStates()
    {
        var texts = Enum.GetValues<ConnectionState>().Select(StatusFormatter.ConnectionText).ToList();

        Assert.All(texts, t => Assert.False(string.IsNullOrWhiteSpace(t)));
        Assert.Equal(texts.Count, texts.Distinct().Count());
    }
}

public class SettingsTests
{
    [Fact]
    public void Defaults_AreConservative()
    {
        var s = new AppSettings();

        Assert.True(s.DelayBetweenRequestsMs >= 1000);
        Assert.InRange(s.DefaultLength, GenerationOptions.MinSupportedLength, GenerationOptions.MaxSupportedLength);
        Assert.True(s.RequestTimeoutSeconds > 0);
    }

    [Fact]
    public void Normalize_ClampsOutOfRangeValues()
    {
        var s = new AppSettings
        {
            DefaultQuantity = 999_999,
            DefaultLength = 99,
            DelayBetweenRequestsMs = 1,
            BatchSize = 5000,
            RequestTimeoutSeconds = 0,
            MaxRetriesOnRateLimit = -4,
            CsvDelimiter = '|',
            Theme = " "
        }.Normalize();

        Assert.Equal(5000, s.DefaultQuantity);
        Assert.Equal(GenerationOptions.MaxSupportedLength, s.DefaultLength);
        Assert.Equal(500, s.DelayBetweenRequestsMs);
        Assert.Equal(100, s.BatchSize);
        Assert.Equal(5, s.RequestTimeoutSeconds);
        Assert.Equal(0, s.MaxRetriesOnRateLimit);
        Assert.Equal(';', s.CsvDelimiter);
        Assert.Equal("DarkNeon", s.Theme);
    }

    [Fact]
    public void SettingsState_UpdateNormalizesCopiesAndRaisesEvent()
    {
        var state = new SettingsState();
        var raised = 0;
        state.Changed += (_, _) => raised++;
        var source = new AppSettings { BatchSize = 9999 };

        state.Update(source);
        source.BatchSize = 1;

        Assert.Equal(1, raised);
        Assert.Equal(100, state.Current.BatchSize);
    }
}

public class SessionStateTests
{
    private static LookupResult R(string name, LookupStatus status) =>
        new(name, status, LookupFailureReason.None, "x", DateTimeOffset.Now);

    [Fact]
    public void Apply_UpdatesCountersAndReplacesPreviousStatus()
    {
        var state = new SessionState();

        state.Apply(R("aaa", LookupStatus.AccountFound));
        state.Apply(R("bbb", LookupStatus.NoPublicMatch));
        state.Apply(R("ccc", LookupStatus.ConnectionError));
        state.Apply(R("ddd", LookupStatus.Inconclusive));

        Assert.Equal(1, state.AccountsFound);
        Assert.Equal(1, state.NoPublicMatch);
        Assert.Equal(2, state.Errors);
        Assert.Equal(2, state.ChecksCompleted);

        state.Apply(R("aaa", LookupStatus.NoPublicMatch));

        Assert.Equal(0, state.AccountsFound);
        Assert.Equal(2, state.NoPublicMatch);
        Assert.Equal(4, state.Results.Count);
    }

    [Fact]
    public void ApplyLoaded_DoesNotCountAsSessionCheck()
    {
        var state = new SessionState();

        state.ApplyLoaded(R("aaa", LookupStatus.AccountFound));

        Assert.Equal(1, state.AccountsFound);
        Assert.Equal(0, state.ChecksCompleted);
    }

    [Fact]
    public void MarkCheckingAndReset_AdjustCounters()
    {
        var state = new SessionState();
        state.Apply(R("aaa", LookupStatus.AccountFound));
        var row = state.Find("aaa")!;

        state.MarkChecking(row);
        Assert.Equal(0, state.AccountsFound);
        Assert.Equal(LookupStatus.Checking, row.Status);

        state.ResetPending(row, "cancelado");
        Assert.Equal(LookupStatus.NotChecked, row.Status);
        Assert.Equal("cancelado", row.Details);
    }

    [Fact]
    public void ProgressPercent_FollowsRunLifecycle()
    {
        var state = new SessionState();
        Assert.Equal(0, state.ProgressPercent);

        state.BeginRun(4);
        Assert.True(state.IsRunning);
        state.AdvanceProgress(1);
        Assert.Equal(25, state.ProgressPercent);
        state.AdvanceProgress(100);
        Assert.Equal(100, state.ProgressPercent);

        state.EndRun();
        Assert.False(state.IsRunning);
    }

    [Fact]
    public void Favorites_AreAppliedToExistingAndNewRows()
    {
        var state = new SessionState();
        state.GetOrAdd("aaa");

        state.SetFavorites(new[] { "aaa", "zzz" });

        Assert.True(state.Find("aaa")!.IsFavorite);
        Assert.NotNull(state.Find("zzz"));
        Assert.True(state.GetOrAdd("zzz").IsFavorite);

        state.SetFavorite(state.Find("aaa")!, false);
        Assert.False(state.Find("aaa")!.IsFavorite);

        state.ClearFavorites();
        Assert.False(state.Find("zzz")!.IsFavorite);
    }

    [Fact]
    public void ClearResults_EmptiesTableAndRowCounters()
    {
        var state = new SessionState();
        state.Apply(R("aaa", LookupStatus.AccountFound));
        state.RegisterGenerated(10);

        state.ClearResults();

        Assert.Empty(state.Results);
        Assert.Equal(0, state.AccountsFound);
        Assert.Null(state.Find("aaa"));
        Assert.Equal(10, state.TotalGenerated);
    }
}

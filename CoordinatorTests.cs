using NyvexGeneration.Core;
using NyvexGeneration.Models;
using NyvexGeneration.Services;
using Xunit;

namespace NyvexGeneration.Tests;

public class CoordinatorTests
{
    private sealed class Harness
    {
        public SessionState State { get; } = new();

        public SettingsState Settings { get; } = new();

        public FakeLookupHistory History { get; } = new();

        public FakeStats Stats { get; } = new();

        public List<TimeSpan> Delays { get; } = new();

        public required FakeApi Api { get; init; }

        public CheckCoordinator Coordinator { get; private set; } = null!;

        public static Harness Create(
            Func<IReadOnlyCollection<string>, CancellationToken, Task<IReadOnlyList<LookupResult>>> batch,
            AppSettings? settings = null)
        {
            var harness = new Harness { Api = new FakeApi(batch) };
            harness.Settings.Update(settings ?? new AppSettings { BatchSize = 50, DelayBetweenRequestsMs = 500 });
            harness.Coordinator = new CheckCoordinator(
                harness.Api, new UsernameValidator(), harness.State, harness.Settings, new TestLog(),
                harness.History, harness.Stats,
                (t, _) =>
                {
                    harness.Delays.Add(t);
                    return Task.CompletedTask;
                });
            return harness;
        }
    }

    private static Task<IReadOnlyList<LookupResult>> AllNoMatch(IReadOnlyCollection<string> names, CancellationToken _) =>
        Task.FromResult<IReadOnlyList<LookupResult>>(
            names.Select(n => FakeApi.Result(n, LookupStatus.NoPublicMatch)).ToList());

    [Fact]
    public async Task ChecksUsernamesAndUpdatesCountersHistoryAndStats()
    {
        var h = Harness.Create((names, _) => Task.FromResult<IReadOnlyList<LookupResult>>(
            names.Select(n => FakeApi.Result(n, n == "aaa" ? LookupStatus.AccountFound : LookupStatus.NoPublicMatch)).ToList()));

        var delivered = await h.Coordinator.RunAsync(new[] { "aaa", "bbb", "ccc" }, force: false);

        Assert.Equal(3, delivered);
        Assert.Equal(1, h.State.AccountsFound);
        Assert.Equal(2, h.State.NoPublicMatch);
        Assert.Equal(3, h.State.ChecksCompleted);
        Assert.Equal(0, h.State.Errors);
        Assert.Equal(3, h.State.ProgressDone);
        Assert.False(h.State.IsRunning);
        Assert.Equal(3, h.History.Saved.Count);
        Assert.Equal(1, h.Stats.Values[StatKeys.AccountsFound]);
        Assert.Equal(2, h.Stats.Values[StatKeys.NoPublicMatch]);
        Assert.Equal(ConnectionState.Online, h.State.Connection);
    }

    [Fact]
    public async Task NormalizesAndDeduplicatesWithinRun()
    {
        var h = Harness.Create(AllNoMatch);

        await h.Coordinator.RunAsync(new[] { "ABC", "abc", " abc ", "Abc" }, force: false);

        var call = Assert.Single(h.Api.Calls);
        Assert.Equal(new[] { "abc" }, call);
    }

    [Fact]
    public async Task SkipsInvalidUsernames()
    {
        var h = Harness.Create(AllNoMatch);

        var delivered = await h.Coordinator.RunAsync(new[] { "ab", "1234", "_abc", "ok1" }, force: false);

        Assert.Equal(1, delivered);
        Assert.Equal(new[] { "ok1" }, h.Api.Calls.Single());
    }

    [Fact]
    public async Task SkipsAlreadyConclusiveUnlessForced()
    {
        var h = Harness.Create(AllNoMatch);
        await h.Coordinator.RunAsync(new[] { "aaa" }, force: false);

        var second = await h.Coordinator.RunAsync(new[] { "aaa", "bbb" }, force: false);
        Assert.Equal(1, second);
        Assert.Equal(new[] { "bbb" }, h.Api.Calls.Last());

        var forced = await h.Coordinator.RunAsync(new[] { "aaa" }, force: true);
        Assert.Equal(1, forced);
        Assert.Equal(new[] { "aaa" }, h.Api.Calls.Last());
    }

    [Fact]
    public async Task RetriesInconclusiveEvenWithoutForce()
    {
        var first = true;
        var h = Harness.Create((names, _) => Task.FromResult<IReadOnlyList<LookupResult>>(
            names.Select(n => FakeApi.Result(n,
                first ? LookupStatus.Inconclusive : LookupStatus.NoPublicMatch,
                first ? LookupFailureReason.HttpError : LookupFailureReason.None)).ToList()));

        await h.Coordinator.RunAsync(new[] { "aaa" }, force: false);
        Assert.Equal(1, h.State.Errors);

        first = false;
        await h.Coordinator.RunAsync(new[] { "aaa" }, force: false);

        Assert.Equal(0, h.State.Errors);
        Assert.Equal(1, h.State.NoPublicMatch);
    }

    [Fact]
    public async Task NothingToDoReturnsZeroWithoutCallingApi()
    {
        var h = Harness.Create(AllNoMatch);

        var delivered = await h.Coordinator.RunAsync(new[] { "ab", "", "  " }, force: false);

        Assert.Equal(0, delivered);
        Assert.Empty(h.Api.Calls);
        Assert.False(h.State.IsRunning);
    }

    [Fact]
    public async Task SplitsIntoBatchesWithDelayBetweenThem()
    {
        var h = Harness.Create(AllNoMatch, new AppSettings { BatchSize = 2, DelayBetweenRequestsMs = 800 });

        await h.Coordinator.RunAsync(new[] { "aaa", "bbb", "ccc", "ddd", "eee" }, force: false);

        Assert.Equal(3, h.Api.Calls.Count);
        Assert.Equal(new[] { 2, 2, 1 }, h.Api.Calls.Select(c => c.Length));
        Assert.Equal(2, h.Delays.Count);
        Assert.All(h.Delays, d => Assert.Equal(TimeSpan.FromMilliseconds(800), d));
    }

    [Fact]
    public async Task RateLimitedChunkIncreasesDelayAndSetsConnectionState()
    {
        var calls = 0;
        var h = Harness.Create((names, _) =>
        {
            calls++;
            var limited = calls == 1;
            return Task.FromResult<IReadOnlyList<LookupResult>>(names.Select(n => FakeApi.Result(
                n,
                limited ? LookupStatus.Inconclusive : LookupStatus.NoPublicMatch,
                limited ? LookupFailureReason.RateLimited : LookupFailureReason.None)).ToList());
        }, new AppSettings { BatchSize = 1, DelayBetweenRequestsMs = 1000 });

        await h.Coordinator.RunAsync(new[] { "aaa", "bbb" }, force: false);

        Assert.Equal(TimeSpan.FromMilliseconds(5000), Assert.Single(h.Delays));
        Assert.Equal(1, h.State.Errors);
        Assert.Equal(ConnectionState.Online, h.State.Connection); // o último lote foi bem-sucedido
    }

    [Fact]
    public async Task AllConnectionErrorsMarkOffline()
    {
        var h = Harness.Create((names, _) => Task.FromResult<IReadOnlyList<LookupResult>>(
            names.Select(n => FakeApi.Result(n, LookupStatus.ConnectionError, LookupFailureReason.Network)).ToList()));

        await h.Coordinator.RunAsync(new[] { "aaa", "bbb" }, force: false);

        Assert.Equal(ConnectionState.Offline, h.State.Connection);
        Assert.Equal(2, h.State.Errors);
        Assert.Equal(0, h.State.ChecksCompleted);
    }

    [Fact]
    public async Task CancellationKeepsDeliveredResultsAndResetsPendingRows()
    {
        using var cts = new CancellationTokenSource();
        var h = Harness.Create((names, ct) =>
        {
            cts.Cancel();
            return AllNoMatch(names, ct);
        }, new AppSettings { BatchSize = 1, DelayBetweenRequestsMs = 500 });

        var delivered = await h.Coordinator.RunAsync(new[] { "aaa", "bbb", "ccc" }, force: false, cts.Token);

        Assert.Equal(1, delivered);
        Assert.Equal(LookupStatus.NoPublicMatch, h.State.Find("aaa")!.Status);
        Assert.Equal(LookupStatus.NotChecked, h.State.Find("bbb")!.Status);
        Assert.Equal(LookupStatus.NotChecked, h.State.Find("ccc")!.Status);
        Assert.False(h.State.IsRunning);
        Assert.False(h.Coordinator.IsRunning);
        Assert.Single(h.History.Saved);
    }

    [Fact]
    public async Task ApiCancellationExceptionIsHandledGracefully()
    {
        var h = Harness.Create((_, ct) => throw new OperationCanceledException());

        var delivered = await h.Coordinator.RunAsync(new[] { "aaa" }, force: false);

        Assert.Equal(0, delivered);
        Assert.Equal(LookupStatus.NotChecked, h.State.Find("aaa")!.Status);
        Assert.False(h.Coordinator.IsRunning);
    }

    [Fact]
    public async Task UnexpectedExceptionDoesNotCrashAndReleasesTheRunLock()
    {
        var fail = true;
        var h = Harness.Create((names, ct) => fail ? throw new InvalidOperationException("boom") : AllNoMatch(names, ct));

        var first = await h.Coordinator.RunAsync(new[] { "aaa" }, force: false);
        Assert.Equal(0, first);
        Assert.Equal(LookupStatus.NotChecked, h.State.Find("aaa")!.Status);

        fail = false;
        var second = await h.Coordinator.RunAsync(new[] { "aaa" }, force: false);
        Assert.Equal(1, second);
    }

    [Fact]
    public async Task SecondRunWhileRunningIsRejected()
    {
        var gate = new TaskCompletionSource();
        var h = Harness.Create(async (names, ct) =>
        {
            await gate.Task;
            return await AllNoMatch(names, ct);
        });

        var firstRun = h.Coordinator.RunAsync(new[] { "aaa" }, force: false);
        Assert.True(h.Coordinator.IsRunning);

        var rejected = await h.Coordinator.RunAsync(new[] { "bbb" }, force: false);
        Assert.Equal(0, rejected);

        gate.SetResult();
        Assert.Equal(1, await firstRun);
        Assert.False(h.Coordinator.IsRunning);
    }

    [Fact]
    public async Task CancelMethodStopsTheRun()
    {
        var started = new TaskCompletionSource();
        var h = Harness.Create(async (names, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return await AllNoMatch(names, ct);
        });

        var run = h.Coordinator.RunAsync(new[] { "aaa" }, force: false);
        await started.Task;
        h.Coordinator.Cancel();

        Assert.Equal(0, await run);
        Assert.Equal(LookupStatus.NotChecked, h.State.Find("aaa")!.Status);
    }
}

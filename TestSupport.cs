using System.Net;
using System.Text;
using NyvexGeneration.Core;
using NyvexGeneration.Data;
using NyvexGeneration.Models;
using NyvexGeneration.Services;

namespace NyvexGeneration.Tests;

internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string?, CancellationToken, Task<HttpResponseMessage>> _responder;

    public FakeHandler(Func<HttpRequestMessage, string?, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _responder = responder;
    }

    public List<string> Bodies { get; } = new();

    public List<HttpRequestMessage> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(request);
        if (body is not null)
        {
            Bodies.Add(body);
        }

        return await _responder(request, body, cancellationToken);
    }

    public static HttpResponseMessage Json(HttpStatusCode code, string json) => new(code)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}

internal sealed class TestLog : ILogService
{
    public List<LogEntry> Entries { get; } = new();

    public event EventHandler<LogEntry>? EntryAdded;

    public void Log(LogSeverity severity, string category, string message, string? technicalDetails = null)
    {
        var entry = new LogEntry(DateTimeOffset.Now, severity, category, message, technicalDetails);
        Entries.Add(entry);
        EntryAdded?.Invoke(this, entry);
    }

    public IReadOnlyList<LogEntry> GetRecent(int count) => Entries.TakeLast(count).ToList();

    public void Clear() => Entries.Clear();
}

internal sealed class FakeApi : IRobloxApiService
{
    private readonly Func<IReadOnlyCollection<string>, CancellationToken, Task<IReadOnlyList<LookupResult>>> _batch;

    public FakeApi(Func<IReadOnlyCollection<string>, CancellationToken, Task<IReadOnlyList<LookupResult>>> batch)
    {
        _batch = batch;
    }

    public List<string[]> Calls { get; } = new();

    public async Task<LookupResult> CheckUsernameAsync(string username, CancellationToken cancellationToken) =>
        (await CheckBatchAsync(new[] { username }, cancellationToken))[0];

    public Task<IReadOnlyList<LookupResult>> CheckBatchAsync(IReadOnlyCollection<string> usernames, CancellationToken cancellationToken)
    {
        Calls.Add(usernames.ToArray());
        return _batch(usernames, cancellationToken);
    }

    public static LookupResult Result(string name, LookupStatus status, LookupFailureReason reason = LookupFailureReason.None) =>
        new(name, status, reason, "teste", DateTimeOffset.Now);
}

internal sealed class FakeLookupHistory : ILookupHistoryRepository
{
    public List<LookupResult> Saved { get; } = new();

    public Task SaveAsync(IEnumerable<LookupResult> results, CancellationToken cancellationToken)
    {
        Saved.AddRange(results);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LookupResult>> GetRecentAsync(int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LookupResult>>(Saved.TakeLast(limit).ToList());

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        Saved.Clear();
        return Task.CompletedTask;
    }
}

internal sealed class FakeStats : IStatsRepository
{
    public Dictionary<string, long> Values { get; } = new();

    public Task AddAsync(string key, long delta, CancellationToken cancellationToken)
    {
        Values[key] = Values.GetValueOrDefault(key) + delta;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, long>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, long>>(new Dictionary<string, long>(Values));

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        Values.Clear();
        return Task.CompletedTask;
    }
}

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nyvex-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

using System.Text.RegularExpressions;
using NyvexGeneration.Core;
using NyvexGeneration.Models;

namespace NyvexGeneration.Services;

public sealed class LogService : ILogService
{
    private const int MaxEntries = 2000;

    private static readonly Regex[] Redactions =
    {
        new(@"\.?ROBLOSECURITY\s*[=:]?\s*\S*", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"_\|WARNING:[^\s]*", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"(cookie|authorization|x-api-key|password|senha|token)\s*[:=]\s*(?:Bearer\s+|Basic\s+)?\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled)
    };

    private readonly object _gate = new();
    private readonly List<LogEntry> _entries = new();
    private readonly string? _directory;

    public LogService(string? logsDirectory = null)
    {
        _directory = logsDirectory;
    }

    public event EventHandler<LogEntry>? EntryAdded;

    public void Log(LogSeverity severity, string category, string message, string? technicalDetails = null)
    {
        var entry = new LogEntry(
            DateTimeOffset.Now,
            severity,
            Sanitize(category) ?? "App",
            Sanitize(message) ?? string.Empty,
            Sanitize(technicalDetails));

        lock (_gate)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries)
            {
                _entries.RemoveRange(0, _entries.Count - MaxEntries);
            }

            WriteToFile(entry);
        }

        EntryAdded?.Invoke(this, entry);
    }

    public IReadOnlyList<LogEntry> GetRecent(int count)
    {
        lock (_gate)
        {
            var take = Math.Clamp(count, 0, _entries.Count);
            return _entries.GetRange(_entries.Count - take, take);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    public static string? Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var result = value;
        foreach (var regex in Redactions)
        {
            result = regex.Replace(result, "[REDACTED]");
        }

        return result;
    }

    private void WriteToFile(LogEntry entry)
    {
        if (string.IsNullOrWhiteSpace(_directory))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, $"nyvex-{entry.Timestamp:yyyyMMdd}.log");
            var line = $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{entry.Severity}] [{entry.Category}] {entry.Message}";

            if (!string.IsNullOrEmpty(entry.TechnicalDetails))
            {
                line += " | " + entry.TechnicalDetails;
            }

            line = line.Replace("\r", " ").Replace("\n", " ");
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch (Exception)
        {
            // Falha de disco nunca deve derrubar o aplicativo.
        }
    }
}

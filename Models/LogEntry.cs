using NyvexGeneration.Core;

namespace NyvexGeneration.Models;

public sealed record LogEntry(
    DateTimeOffset Timestamp,
    LogSeverity Severity,
    string Category,
    string Message,
    string? TechnicalDetails = null);

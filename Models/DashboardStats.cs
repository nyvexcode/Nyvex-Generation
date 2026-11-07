namespace NyvexGeneration.Models;

public sealed record DashboardStats(
    int TotalGenerated,
    int AccountsFound,
    int NoPublicMatch,
    int ChecksCompleted,
    int Errors,
    TimeSpan Elapsed);

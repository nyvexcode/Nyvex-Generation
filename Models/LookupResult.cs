using NyvexGeneration.Core;

namespace NyvexGeneration.Models;

public sealed record LookupResult(
    string Username,
    LookupStatus Status,
    LookupFailureReason FailureReason,
    string Details,
    DateTimeOffset CheckedAt,
    long? RobloxUserId = null,
    string? MatchedName = null,
    int? HttpStatusCode = null)
{
    public int Length => Username.Length;

    /// <summary>
    /// Sempre falso por projeto: a busca pública nunca confirma que um username está disponível.
    /// </summary>
    public bool RegistrationAvailabilityConfirmed => false;
}

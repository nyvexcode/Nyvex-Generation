namespace NyvexGeneration.Core;

public enum LookupFailureReason
{
    None = 0,
    RateLimited = 1,
    Timeout = 2,
    Cancelled = 3,
    HttpError = 4,
    InvalidResponse = 5,
    Network = 6
}

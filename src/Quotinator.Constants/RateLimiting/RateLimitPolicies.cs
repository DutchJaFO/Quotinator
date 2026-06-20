namespace Quotinator.Constants.RateLimiting;

/// <summary>Rate-limiter policy names.</summary>
public static class RateLimitPolicies
{
    public const string Api   = "api";
    /// <summary>Concurrency-1 policy for destructive admin operations. Rejects any concurrent call immediately with 429.</summary>
    public const string Admin = "admin";
}

namespace Quotinator.Data.Connections;

/// <summary>
/// Keyed-DI service keys for <see cref="IDbConnectionFactory"/> registrations beyond the default
/// (unkeyed) main database registration.
/// </summary>
public static class DatabaseConnectionKeys
{
    /// <summary>
    /// Key for the separate, in-memory changelog database (#309) — no relational or transactional
    /// coupling to domain data, so it lives outside the main database (ADR 018).
    /// </summary>
    public const string Changelog = "changelog";
}

namespace Quotinator.Constants.Api;

/// <summary>Constants for OpenAPI tag names. Keeps tag values consistent between the document transformer and endpoint declarations.</summary>
public static class ApiTags
{
    public const string System        = "System";
    public const string Quotes        = "Quotes";
    public const string Admin         = "Admin";
    public const string Import        = "Import";
    public const string Conversations = "Conversations";
    public const string MasterData    = "MasterData";
    public const string Notifications = "Notifications";

    /// <summary>
    /// Backup management (#349). Deliberately its own category rather than <see cref="Admin"/>, which
    /// every <c>/api/v1/admin/**</c> route otherwise carries: backup management is a distinct operator
    /// task, and burying five endpoints among reseed/reset/audit hides them from exactly the operator
    /// who came looking because a backup could not be taken. Access is unchanged — the same API key and
    /// the same concurrency-1 limiter; this is a documentation grouping only.
    /// </summary>
    public const string Backup        = "Backup";
}

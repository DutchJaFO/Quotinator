namespace Quotinator.Data.Database;

/// <summary>
/// The name each database is reported under (#305, #490): by its initializer's integrity check and by its
/// database check, so the two cannot name the same database differently.
/// </summary>
public static class DatabaseNames
{
    /// <summary>The main database, holding the application's content.</summary>
    public const string Main = "main";

    /// <summary>The changelog database (#309, ADR 018).</summary>
    public const string Changelog = "changelog";
}

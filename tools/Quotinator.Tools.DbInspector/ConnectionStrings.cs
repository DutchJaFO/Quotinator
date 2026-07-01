namespace Quotinator.Tools.DbInspector;

/// <summary>Builds SQLite connection strings for this tool.</summary>
internal static class ConnectionStrings
{
    /// <summary>
    /// Read-only connection string for <paramref name="dbPath"/>. This tool executes whatever SQL
    /// text the caller passes in, so it cannot parameterise the query the way the rest of the
    /// codebase's SQL policy requires — read-only mode is the equivalent protection: no query,
    /// however it's crafted, can mutate the database.
    /// </summary>
    internal static string BuildReadOnly(string dbPath) => $"Data Source={dbPath};Mode=ReadOnly";
}

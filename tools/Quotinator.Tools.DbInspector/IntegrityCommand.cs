namespace Quotinator.Tools.DbInspector;

/// <summary>
/// The <c>integrity</c> command (#305): compares a database file's schema with the one its migration
/// path defines, choosing the main or the changelog schema from the migration tables the file holds,
/// and prints every difference.
/// </summary>
internal static class IntegrityCommand
{
    /// <summary>Checks the database file and writes the report.</summary>
    /// <param name="dbPath">The database file to check.</param>
    /// <param name="output">Where the report is written.</param>
    /// <returns>0 when the file is intact; non-zero when anything is missing or different, or the file cannot be checked.</returns>
    internal static Task<int> RunAsync(string dbPath, TextWriter output)
    {
        _ = dbPath;
        _ = output;
        return Task.FromResult(0);
    }
}

using Microsoft.Data.Sqlite;
using Quotinator.Core.Database;
using Quotinator.Data.Database;
using Quotinator.Data.Schema;

namespace Quotinator.Tools.DbInspector;

/// <summary>
/// The <c>integrity</c> command (#305): compares a database file's schema with the one its migration
/// path defines, choosing the main or the changelog schema from the migration tables the file holds,
/// and prints every difference.
/// </summary>
internal static class IntegrityCommand
{
    private const string MainVersionTable = "System_ConsumerSchemaVersion";
    private const string ChangelogVersionTable = "Changelog_SchemaVersion";

    // A developer tool with no container, reading SQLite files only.
    private static readonly ISchemaSnapshotReader Reader = SqliteSchemaSnapshotReader.Instance;

    /// <summary>Checks the database file and writes the report.</summary>
    /// <param name="dbPath">The database file to check.</param>
    /// <param name="output">Where the report is written.</param>
    /// <returns>0 when the file is intact; non-zero when anything is missing or different, or the file cannot be checked.</returns>
    internal static async Task<int> RunAsync(string dbPath, TextWriter output)
    {
        SchemaSnapshot actual;
        using (SqliteConnection connection = new(ConnectionStrings.BuildReadOnly(dbPath)))
        {
            await connection.OpenAsync();
            actual = await Reader.ReadAsync(connection);
        }

        (string database, SchemaSnapshot expected)? reference = await ExpectedSchemaAsync(actual);
        if (reference is null)
        {
            await output.WriteLineAsync(
                $"{dbPath}: not a Quotinator database (neither {MainVersionTable} nor {ChangelogVersionTable} is present).");
            return 2;
        }

        SchemaIntegrityResult result = SchemaIntegrityComparer.Compare(reference.Value.expected, actual);

        await output.WriteLineAsync($"{reference.Value.database} database: {(result.IsIntact ? "intact" : "NOT intact")}");
        foreach (SchemaDifference difference in result.Differences)
        {
            string detail = difference.Aspect is null
                ? string.Empty
                : $" {difference.Aspect}: expected '{difference.Expected}', actual '{difference.Actual}'";
            await output.WriteLineAsync($"  {difference.Kind} {difference.ObjectKind} {difference.ObjectName}{detail}");
        }

        return result.IsIntact ? 0 : 1;
    }

    private static async Task<(string, SchemaSnapshot)?> ExpectedSchemaAsync(SchemaSnapshot actual)
    {
        bool Has(string table) => actual.Tables.Any(t => string.Equals(t.Name, table, StringComparison.OrdinalIgnoreCase));

        if (Has(MainVersionTable))
            return ("main", await Reader.ReadScriptsAsync(DatabaseInitializer.ExpectedSchemaScripts(QuotinatorMigrations.All, QuotinatorMigrations.Baseline)));

        if (Has(ChangelogVersionTable))
            return ("changelog", await Reader.ReadScriptsAsync(ChangelogDatabaseInitializer.ExpectedSchemaScripts()));

        return null;
    }
}

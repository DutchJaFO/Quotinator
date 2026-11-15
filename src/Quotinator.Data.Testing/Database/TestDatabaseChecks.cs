using Quotinator.Data.Database;
using Quotinator.Data.Database.Checks;
using Quotinator.Data.Enums;

namespace Quotinator.Data.Testing.Database;

/// <summary>
/// The real SQLite database check for a test's database file (#490), built the way <c>Program.cs</c> builds the
/// registered one: the reserved work folder beside the file, and the expected schema from the same migration
/// path the test's initializer uses.
/// </summary>
public static class TestDatabaseChecks
{
    /// <summary>The check for the main database at <paramref name="dbPath"/>.</summary>
    /// <param name="dbPath">The database file.</param>
    /// <param name="migrations">The consumer migrations the test's initializer uses.</param>
    /// <param name="baseline">The consumer baseline the test's initializer uses, or <see langword="null"/>.</param>
    public static SqliteDatabaseCheck ForMain(string dbPath, IReadOnlyList<SchemaMigration> migrations, SchemaBaseline? baseline) =>
        For(DatabaseNames.Main, dbPath, DatabaseInitializer.ExpectedSchemaScripts(migrations, baseline),
            DatabaseInitializer.ExpectedSchemaVersions(migrations));

    /// <summary>The check for the changelog database at <paramref name="dbPath"/>.</summary>
    /// <param name="dbPath">The database file.</param>
    public static SqliteDatabaseCheck ForChangelog(string dbPath) =>
        For(DatabaseNames.Changelog, dbPath, ChangelogDatabaseInitializer.ExpectedSchemaScripts(),
            ChangelogDatabaseInitializer.ExpectedSchemaVersions());

    private static SqliteDatabaseCheck For(
        string name, string dbPath, IReadOnlyList<string> expectedSchemaScripts,
        IReadOnlyDictionary<SchemaVersionTable, int> expectedSchemaVersions) =>
        new(name, dbPath,
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath))!, SqliteDatabaseCheck.WorkFolderName),
            expectedSchemaScripts, expectedSchemaVersions);
}

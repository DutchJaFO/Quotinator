using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Core.Database;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Testing.Database;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Tools.DbInspector.Tests;

/// <summary>
/// #305: the <c>integrity</c> command checks a database file without a running application, choosing the
/// main or the changelog schema from the migration tables the file holds, and exits non-zero when anything
/// is missing or different, so a script can depend on it.
/// </summary>
[TestClass]
public class IntegrityCommandTests
{
    private TempDirectory _tempDir = null!;

    [TestInitialize]
    public void TestInitialize() => _tempDir = new TempDirectory("quotinator_305_dbinspector_");

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        _tempDir.Dispose();
    }

    [TestMethod]
    public async Task IntactFile_ExitsZero()
    {
        string file = await MainDatabaseAsync();
        using StringWriter output = new();

        int exitCode = await IntegrityCommand.RunAsync(file, output);

        Assert.AreEqual(0, exitCode, output.ToString());
        Assert.Contains("intact", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task MissingTable_ReportsItAndExitsNonZero()
    {
        string file = await MainDatabaseAsync();
        Execute(file, "DROP TABLE System_Notification;");
        using StringWriter output = new();

        int exitCode = await IntegrityCommand.RunAsync(file, output);

        Assert.AreNotEqual(0, exitCode);
        Assert.Contains("System_Notification", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A changelog file compared against the main schema would report every main table missing; compared
    /// against its own, it is intact. Losing one of its own tables is the control that it is really checked.
    /// </summary>
    [TestMethod]
    public async Task ChangelogFile_ComparedAgainstTheChangelogSchema()
    {
        string file = Path.Combine(_tempDir.Path, "changelog.db");
        await new ChangelogDatabaseInitializer(new SqliteConnectionFactory(file), NullLogger<ChangelogDatabaseInitializer>.Instance)
            .InitialiseAsync();
        SqliteConnection.ClearAllPools();

        using StringWriter intactOutput = new();
        Assert.AreEqual(0, await IntegrityCommand.RunAsync(file, intactOutput), intactOutput.ToString());
        Assert.Contains("changelog", intactOutput.ToString(), StringComparison.OrdinalIgnoreCase);

        Execute(file, "DROP TABLE Changelog_Line;");
        using StringWriter brokenOutput = new();
        Assert.AreNotEqual(0, await IntegrityCommand.RunAsync(file, brokenOutput));
        Assert.Contains("Changelog_Line", brokenOutput.ToString(), StringComparison.Ordinal);
    }

    /// <summary>The main schema, built by the base initializer with this application's own migration path and no content.</summary>
    private async Task<string> MainDatabaseAsync()
    {
        string file = Path.Combine(_tempDir.Path, "main.db");
        DatabaseInitializer initializer = new(
            new SqliteConnectionFactory(file),
            new DatabaseOptions { DbPath = file, BackupsPath = Path.Combine(_tempDir.Path, "backups") },
            QuotinatorMigrations.All,
            NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance,
            NullLogger<DatabaseInitializer>.Instance,
            NoOpDiskSpaceProvider.Instance,
            QuotinatorMigrations.Baseline);
        await initializer.InitialiseAsync();
        SqliteConnection.ClearAllPools();
        return file;
    }

    private static void Execute(string file, string sql)
    {
        using (SqliteConnection connection = new($"Data Source={file}"))
        {
            connection.Open();
            connection.Execute(sql);
        }

        SqliteConnection.ClearAllPools();
    }
}

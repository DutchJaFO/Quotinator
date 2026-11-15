using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Testing.Database;
using Quotinator.Tools.TestProbe.Commands;

namespace Quotinator.Tools.TestProbe.Tests.Commands;

/// <summary>
/// #490: <c>damage --rewrite-last-index-key</c> leaves an index that disagrees with its table, which
/// <c>PRAGMA integrity_check</c> finds and <c>PRAGMA quick_check</c> does not, through the same helper the
/// database check's unit tests use.
/// </summary>
[TestClass]
public sealed class DamageCommandTests
{
    private const string IndexName = "IX_CheckFixture_Rank";

    private TempDirectory _directory = null!;
    private string _dbPath = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _directory = new TempDirectory("quotinator_probe_damage_");
        _dbPath = Path.Combine(_directory.Path, "test.db");
        DocumentedFormatFixtures.Create(_dbPath,
            "CREATE TABLE CheckFixture (Id INTEGER PRIMARY KEY, Rank INTEGER NOT NULL);",
            $"CREATE INDEX {IndexName} ON CheckFixture (Rank);",
            "INSERT INTO CheckFixture (Id, Rank) VALUES (1, 10), (2, 20), (3, 30), (4, 40), (5, 50);");
    }

    [TestCleanup]
    public void TestCleanup() => _directory.Dispose();

    [TestMethod]
    public void RewriteLastIndexKey_LeavesAnIndexThatDisagreesWithItsTable()
    {
        Assert.AreEqual("ok", Pragma("PRAGMA integrity_check;"), "Precondition: the fixture is damaged before the command runs.");

        int exitCode = CommandDispatcher.Run(
            ["damage", "--rewrite-last-index-key", _dbPath, IndexName, "100"], TextReader.Null, TextWriter.Null, TextWriter.Null);

        Assert.AreEqual(0, exitCode);
        Assert.AreNotEqual("ok", Pragma("PRAGMA integrity_check;"), "The index still agrees with its table.");
        Assert.AreEqual("ok", Pragma("PRAGMA quick_check;"), "The damage is visible to quick_check, so it is not the documented kind.");
    }

    /// <summary>A precondition that does not hold is reported as a failure, exit code 1, rather than thrown.</summary>
    [TestMethod]
    public void RewriteLastIndexKey_UnknownIndex_FailsNamingIt()
    {
        StringWriter error = new();

        int exitCode = CommandDispatcher.Run(
            ["damage", "--rewrite-last-index-key", _dbPath, "IX_DoesNotExist", "100"], TextReader.Null, TextWriter.Null, error);

        Assert.AreEqual(1, exitCode);
        Assert.Contains("IX_DoesNotExist", error.ToString());
    }

    /// <summary>A database file that does not exist fails naming it, rather than crashing on SQLite's own error.</summary>
    [TestMethod]
    public void RewriteLastIndexKey_MissingFile_FailsNamingIt()
    {
        string missing = Path.Combine(_directory.Path, "missing.db");
        StringWriter error = new();

        int exitCode = CommandDispatcher.Run(
            ["damage", "--rewrite-last-index-key", missing, IndexName, "100"], TextReader.Null, TextWriter.Null, error);

        Assert.AreEqual(1, exitCode);
        Assert.Contains("missing.db", error.ToString());
    }

    [TestMethod]
    public void UnknownOperation_Fails()
    {
        Assert.AreEqual(1, CommandDispatcher.Run(["damage", "--melt", _dbPath], TextReader.Null, TextWriter.Null, TextWriter.Null));
    }

    private string Pragma(string pragma)
    {
        using SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = new Uri(_dbPath).AbsoluteUri + "?immutable=1",
            Mode       = SqliteOpenMode.ReadOnly,
            Pooling    = false,
        }.ToString());
        connection.Open();
        return string.Join("; ", connection.Query<string>(pragma));
    }
}

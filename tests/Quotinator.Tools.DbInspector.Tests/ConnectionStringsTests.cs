using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Tools.DbInspector;

namespace Quotinator.Tools.DbInspector.Tests;

[TestClass]
public class ConnectionStringsTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_dbinspector_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");

        using var setup = new SqliteConnection($"Data Source={_dbPath}");
        setup.Open();
        setup.Execute("CREATE TABLE Widgets (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL)");
        setup.Execute("INSERT INTO Widgets (Name) VALUES ('original')");
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public void BuildReadOnly_SelectQuery_Succeeds()
    {
        using var connection = new SqliteConnection(ConnectionStrings.BuildReadOnly(_dbPath));
        connection.Open();

        var name = connection.QuerySingle<string>("SELECT Name FROM Widgets WHERE Id = 1");

        Assert.AreEqual("original", name);
    }

    [TestMethod]
    public void BuildReadOnly_UpdateQuery_ThrowsReadOnlyException()
    {
        using var connection = new SqliteConnection(ConnectionStrings.BuildReadOnly(_dbPath));
        connection.Open();

        var ex = Assert.ThrowsExactly<SqliteException>(() =>
            connection.Execute("UPDATE Widgets SET Name = 'tampered' WHERE Id = 1"));

        StringAssert.Contains(ex.Message, "readonly");
    }

    [TestMethod]
    public void BuildReadOnly_InsertQuery_ThrowsReadOnlyException()
    {
        using var connection = new SqliteConnection(ConnectionStrings.BuildReadOnly(_dbPath));
        connection.Open();

        Assert.ThrowsExactly<SqliteException>(() =>
            connection.Execute("INSERT INTO Widgets (Name) VALUES ('new')"));
    }

    [TestMethod]
    public void BuildReadOnly_DataUnchangedAfterFailedWriteAttempt()
    {
        using var connection = new SqliteConnection(ConnectionStrings.BuildReadOnly(_dbPath));
        connection.Open();

        try { connection.Execute("UPDATE Widgets SET Name = 'tampered' WHERE Id = 1"); }
        catch (SqliteException) { }

        var name = connection.QuerySingle<string>("SELECT Name FROM Widgets WHERE Id = 1");
        Assert.AreEqual("original", name);
    }

    /// <summary>
    /// Quotinator's real databases run in WAL mode (see <c>DatabaseInitializer.EnableWal</c>).
    /// SQLite's WAL implementation has a documented quirk: a read-only connection can only open a
    /// WAL-mode database if the -shm/-wal files already exist (or can be created) — see
    /// https://sqlite.org/wal.html. This test replicates that exact scenario (WAL enabled, -shm/-wal
    /// files present from a prior write) rather than the default rollback-journal mode the other
    /// tests in this class use, so the read-only guarantee is verified against what production
    /// databases actually look like, not just a default-mode SQLite file.
    /// </summary>
    [TestMethod]
    public void BuildReadOnly_WalModeDatabaseWithExistingShmWalFiles_OpensAndStaysReadOnly()
    {
        using (var setup = new SqliteConnection($"Data Source={_dbPath}"))
        {
            setup.Open();
            setup.Execute("PRAGMA journal_mode=WAL;");
            setup.Execute("INSERT INTO Widgets (Name) VALUES ('wal-written')");
        }

        Assert.IsTrue(File.Exists($"{_dbPath}-wal"), "Expected a -wal file after a WAL-mode write");
        Assert.IsTrue(File.Exists($"{_dbPath}-shm"), "Expected a -shm file after a WAL-mode write");

        using var readOnly = new SqliteConnection(ConnectionStrings.BuildReadOnly(_dbPath));
        readOnly.Open();

        var count = readOnly.QuerySingle<int>("SELECT COUNT(*) FROM Widgets");
        Assert.AreEqual(2, count, "Should see both the original row and the WAL-mode write");

        Assert.ThrowsExactly<SqliteException>(() =>
            readOnly.Execute("UPDATE Widgets SET Name = 'tampered' WHERE Id = 1"));
    }
}

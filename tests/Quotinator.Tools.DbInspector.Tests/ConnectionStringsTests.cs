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
}

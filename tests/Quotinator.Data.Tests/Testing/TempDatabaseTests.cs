using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Testing.Database;

namespace Quotinator.Data.Tests.Testing;

[TestClass]
public class TempDatabaseTests
{
    [TestMethod]
    public void TempDatabase_CreatesFileAndAppliesDdl()
    {
        const string ddl = "CREATE TABLE Foo (Id TEXT NOT NULL PRIMARY KEY);";

        using var db = new TempDatabase([ddl]);

        Assert.IsTrue(File.Exists(db.DbPath));

        using var conn = new SqliteConnection($"Data Source={db.DbPath}");
        conn.Open();
        var count = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Foo';");
        Assert.AreEqual(1, count);
    }

    [TestMethod]
    public void Dispose_DeletesTempDirectory()
    {
        string dbPath;
        string? dir;

        using (var db = new TempDatabase([]))
        {
            dbPath = db.DbPath;
            dir    = Path.GetDirectoryName(dbPath);
        }

        Assert.IsFalse(Directory.Exists(dir), "Temp directory should be deleted on Dispose.");
    }

    [TestMethod]
    public void ConnectionFactory_CanOpenConnection()
    {
        using var db   = new TempDatabase([]);
        using var conn = db.ConnectionFactory.CreateConnection();
        conn.Open();

        Assert.AreEqual(System.Data.ConnectionState.Open, conn.State);
    }
}

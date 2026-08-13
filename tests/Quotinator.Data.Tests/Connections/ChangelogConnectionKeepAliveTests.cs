using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;

namespace Quotinator.Data.Tests.Connections;

/// <summary>
/// Exercises <see cref="ChangelogConnectionKeepAlive"/>'s reason for existing — a shared-cache
/// in-memory SQLite database is destroyed the moment its last open connection closes, so without a
/// held-open connection, separately-opened connections to the same shared-cache name would not see
/// each other's data.
/// </summary>
[TestClass]
public class ChangelogConnectionKeepAliveTests
{
    [TestCleanup]
    public void TestCleanup() => SqliteConnection.ClearAllPools();

    private static string UniqueConnectionString() =>
        $"file:{Guid.NewGuid():N}?mode=memory&cache=shared";

    [TestMethod]
    public void MultipleConnections_WithKeepAliveOpen_ShareSameInMemoryDatabase()
    {
        var factory = new SqliteConnectionFactory(UniqueConnectionString());
        using var keepAlive = new ChangelogConnectionKeepAlive(factory);

        using (var writer = (SqliteConnection)factory.CreateConnection())
        {
            writer.Open();
            writer.Execute("CREATE TABLE T (Id INTEGER PRIMARY KEY); INSERT INTO T (Id) VALUES (1);");
        }

        using var reader = (SqliteConnection)factory.CreateConnection();
        reader.Open();
        var count = reader.ExecuteScalar<int>("SELECT COUNT(*) FROM T;");

        Assert.AreEqual(1, count, "A separately-opened connection to the same shared-cache name must see data written by another connection while the keep-alive connection stays open.");
    }

    /// <summary>
    /// Disposing a <see cref="SqliteConnection"/> returns it to Microsoft.Data.Sqlite's connection
    /// pool rather than immediately closing the underlying native connection — found live: without
    /// <see cref="SqliteConnection.ClearAllPools"/>, a shared-cache in-memory database survived past
    /// the keep-alive's disposal, because the pool itself kept a dormant connection open. This test
    /// forces the pool to actually let go (matching real conditions the keep-alive must be resilient
    /// to — pooled connections do get reclaimed under memory pressure or an explicit clear elsewhere
    /// in the process) to prove the keep-alive is genuinely load-bearing, not just defensive against a
    /// scenario pooling already prevents on its own.
    /// </summary>
    [TestMethod]
    public void NewConnection_AfterKeepAliveDisposedAndPoolsCleared_SeesEmptyFreshDatabase()
    {
        var factory = new SqliteConnectionFactory(UniqueConnectionString());

        using (var keepAlive = new ChangelogConnectionKeepAlive(factory))
        {
            using var writer = (SqliteConnection)factory.CreateConnection();
            writer.Open();
            writer.Execute("CREATE TABLE T (Id INTEGER PRIMARY KEY); INSERT INTO T (Id) VALUES (1);");
        } // keep-alive disposed here.
        SqliteConnection.ClearAllPools(); // ...and now the pool actually releases its own connection too.

        using var reader = (SqliteConnection)factory.CreateConnection();
        reader.Open();
        var tableExists = reader.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'T';");

        Assert.AreEqual(0, tableExists, "Once every connection to a shared-cache in-memory database closes (including pooled ones), the database is destroyed — a new connection to the same name starts genuinely empty.");
    }
}

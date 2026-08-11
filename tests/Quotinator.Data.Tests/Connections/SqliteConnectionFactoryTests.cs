using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;

namespace Quotinator.Data.Tests.Connections;

/// <summary>Exercises <see cref="Quotinator.Data.Connections.SqliteConnectionFactory"/>'s per-connection setup applied on every <c>Open</c>.</summary>
[TestClass]
public class SqliteConnectionFactoryTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_connection_factory_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>
    /// #293: root cause of a live HA v1.8.2 → v1.8.3-beta migration failure — see the factory's own
    /// comment for the full incident writeup. <c>temp_store</c> is per-connection (unlike
    /// <c>journal_mode=WAL</c>, which persists in the database file itself), so it must be re-applied
    /// on every <c>Open</c>, not just once at startup — proven here directly against a fresh connection,
    /// and again on a second connection to confirm it isn't a one-time artifact of the first Open.
    /// </summary>
    [TestMethod]
    public async Task CreateConnection_OnOpen_SetsTempStoreToMemory()
    {
        var factory = new SqliteConnectionFactory(_dbPath);

        using (var first = (SqliteConnection)factory.CreateConnection())
        {
            first.Open();
            var tempStore = await first.ExecuteScalarAsync<int>("PRAGMA temp_store;");
            Assert.AreEqual(2, tempStore, "temp_store must be MEMORY (2), not the SQLite default (0/FILE)");
        }

        using var second = (SqliteConnection)factory.CreateConnection();
        second.Open();
        var secondTempStore = await second.ExecuteScalarAsync<int>("PRAGMA temp_store;");
        Assert.AreEqual(2, secondTempStore, "temp_store must be re-applied on every new connection, not just the first");
    }

    /// <summary>Regression guard: the existing per-connection UNICODE_CONTAINS registration (#222) must still work alongside the new pragma applied in the same StateChange handler.</summary>
    [TestMethod]
    public async Task CreateConnection_OnOpen_StillRegistersUnicodeContainsFunction()
    {
        var factory = new SqliteConnectionFactory(_dbPath);
        using var connection = (SqliteConnection)factory.CreateConnection();
        connection.Open();

        var result = await connection.ExecuteScalarAsync<bool>("SELECT UNICODE_CONTAINS('café', 'CAFÉ');");

        Assert.IsTrue(result);
    }

    public TestContext TestContext { get; set; }
}

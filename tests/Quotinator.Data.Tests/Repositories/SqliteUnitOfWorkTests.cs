using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Tests.Repositories;

[TestClass]
public class SqliteUnitOfWorkTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_data_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute("CREATE TABLE Widgets (Id TEXT NOT NULL PRIMARY KEY, Label TEXT NOT NULL);");
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Owning constructor (existing behavior, unaffected) ──────────────────────

    [TestMethod]
    public async Task OwningConstructor_BeginTransactionAsync_OpensItsOwnConnection()
    {
        var uow = new SqliteUnitOfWork(new SqliteConnectionFactory(_dbPath));

        await uow.BeginTransactionAsync();

        Assert.AreEqual(System.Data.ConnectionState.Open, ((SqliteConnection)uow.Connection).State);
        await uow.DisposeAsync();
    }

    // ── Wrapping constructor ──────────────────────────────────────────────────

    [TestMethod]
    public void WrappedConstructor_ExposesTheSuppliedConnectionAndTransaction()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var wrapped = new SqliteUnitOfWork(connection, transaction);

        Assert.AreSame(connection, wrapped.Connection);
        Assert.AreSame(transaction, wrapped.Transaction);
    }

    [TestMethod]
    public async Task WrappedConstructor_BeginTransactionAsync_IsNoOp_DoesNotReplaceConnection()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var wrapped = new SqliteUnitOfWork(connection, transaction);

        await wrapped.BeginTransactionAsync();

        Assert.AreSame(connection, wrapped.Connection);
        Assert.AreSame(transaction, wrapped.Transaction);
    }

    [TestMethod]
    public async Task WrappedConstructor_CommitAsync_NeverCommitsTheExternalTransaction()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        await connection.ExecuteAsync("INSERT INTO Widgets (Id, Label) VALUES ('1', 'x');", transaction: transaction);
        var wrapped = new SqliteUnitOfWork(connection, transaction);

        await wrapped.CommitAsync();
        transaction.Rollback();

        var count = await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Widgets;");
        Assert.AreEqual(0, count, "wrapped.CommitAsync() must not have committed the caller-owned transaction");
    }

    [TestMethod]
    public async Task WrappedConstructor_RollbackAsync_NeverRollsBackTheExternalTransaction()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        await connection.ExecuteAsync("INSERT INTO Widgets (Id, Label) VALUES ('1', 'x');", transaction: transaction);
        var wrapped = new SqliteUnitOfWork(connection, transaction);

        await wrapped.RollbackAsync();
        transaction.Commit();

        var count = await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Widgets;");
        Assert.AreEqual(1, count, "wrapped.RollbackAsync() must not have rolled back the caller-owned transaction");
    }

    [TestMethod]
    public async Task WrappedConstructor_DisposeAsync_NeverDisposesTheExternalConnectionOrTransaction()
    {
        var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        var transaction = connection.BeginTransaction();
        var wrapped = new SqliteUnitOfWork(connection, transaction);

        await wrapped.DisposeAsync();

        // Both must still be usable — proves DisposeAsync on the wrapper never touched them.
        await connection.ExecuteAsync("INSERT INTO Widgets (Id, Label) VALUES ('1', 'x');", transaction: transaction);
        transaction.Commit();
        var count = await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Widgets;");
        Assert.AreEqual(1, count);

        transaction.Dispose();
        connection.Dispose();
    }
}

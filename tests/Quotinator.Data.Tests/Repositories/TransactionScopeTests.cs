using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Example.Common;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Data.Tests.Repositories;

[TestClass]
public class TransactionScopeTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;
    private IDbConnectionFactory _factory = null!;
    private SqliteRepository<Widget> _repository = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_scope_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute("""
            CREATE TABLE Widgets (
                Id           TEXT    NOT NULL PRIMARY KEY,
                Label        TEXT    NOT NULL,
                DateCreated  TEXT,
                DateModified TEXT,
                DateDeleted  TEXT,
                IsDeleted    INTEGER NOT NULL DEFAULT 0
            );
            """);

        _factory    = new SqliteConnectionFactory(_dbPath);
        _repository = new SqliteRepository<Widget>(_factory, NoOpAuditWriter.Instance, NoOpCallerContext.Instance);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private int CountWidgets()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Widgets;");
    }

    // ── No existing UoW — creates own, commits on success ─────────────────────

    [TestMethod]
    public async Task ExecuteAsync_NoExisting_CreatesAndCommits()
    {
        var widget = new Widget { Label = "Committed" };

        await TransactionScope.ExecuteAsync(_factory, async uow =>
        {
            await _repository.InsertAsync(widget, uow);
        });

        Assert.AreEqual(1, CountWidgets());
    }

    // ── Existing UoW — joins it, does not commit ──────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_WithExisting_JoinsAndDoesNotCommit()
    {
        var widget = new Widget { Label = "NotYetCommitted" };

        await using var uow = new SqliteUnitOfWork(_factory);
        await uow.BeginTransactionAsync();

        await TransactionScope.ExecuteAsync(_factory, async innerUow =>
        {
            await _repository.InsertAsync(widget, innerUow);
        }, uow);

        // TransactionScope must not have committed — row not visible to other connections.
        Assert.AreEqual(0, CountWidgets(), "Row must not be visible before the owner commits");

        await uow.CommitAsync();

        Assert.AreEqual(1, CountWidgets(), "Row must be visible after the owner commits");
    }

    // ── Exception when owner — rolls back, leaves no rows ────────────────────

    [TestMethod]
    public async Task ExecuteAsync_OnException_Rollback_LeavesNoRows()
    {
        var widget = new Widget { Label = "WillBeRolledBack" };

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await TransactionScope.ExecuteAsync(_factory, async uow =>
            {
                await _repository.InsertAsync(widget, uow);
                throw new InvalidOperationException("Simulated failure");
            }));

        Assert.AreEqual(0, CountWidgets());
    }
}

using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Example.Common;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Tests.Repositories;

[TestClass]
public class InsertManyAsyncTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;
    private IDbConnectionFactory _factory  = null!;
    private AuditWriter _auditWriter = null!;
    private CallerContext _callerContext = null!;
    private SqliteRepository<Widget> _repository = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_many_test_").FullName;
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
            CREATE TABLE AuditEntries (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                TableName   TEXT    NOT NULL,
                RecordId    TEXT,
                Operation   TEXT    NOT NULL,
                Agent       TEXT,
                PerformedAt TEXT    NOT NULL
            );
            """);

        _factory      = new SqliteConnectionFactory(_dbPath);
        _callerContext = new CallerContext();
        _auditWriter  = new AuditWriter(_factory, _callerContext);
        _repository   = new SqliteRepository<Widget>(_factory, _auditWriter, _callerContext);
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
        return conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Widgets WHERE IsDeleted = 0;");
    }

    private int CountAuditEntries()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn.ExecuteScalar<int>("SELECT COUNT(*) FROM AuditEntries;");
    }

    private static List<Widget> MakeWidgets(int count)
        => Enumerable.Range(1, count).Select(i => new Widget { Label = $"Widget {i}" }).ToList();

    // ── IRepository contract ──────────────────────────────────────────────────

    [TestMethod]
    public void SqliteRepository_ImplementsInsertManyAsync()
        => Assert.IsInstanceOfType<IRepository<Widget>>(_repository);

    // ── Bulk strategy ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task InsertManyAsync_Bulk_AllRowsPersisted()
    {
        var widgets = MakeWidgets(5);

        await _repository.InsertManyAsync(widgets, strategy: InsertStrategy.Bulk);

        Assert.AreEqual(5, CountWidgets());
    }

    [TestMethod]
    public async Task InsertManyAsync_Bulk_AuditEntriesWrittenForAll()
    {
        var widgets = MakeWidgets(4);

        await _repository.InsertManyAsync(widgets, strategy: InsertStrategy.Bulk);

        Assert.AreEqual(4, CountAuditEntries());
    }

    [TestMethod]
    public async Task InsertManyAsync_Bulk_AuditEntriesAreInsertOperation()
    {
        var widgets = MakeWidgets(3);

        await _repository.InsertManyAsync(widgets, strategy: InsertStrategy.Bulk);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var operations = conn.Query<string>("SELECT Operation FROM AuditEntries;").ToList();

        Assert.IsTrue(operations.All(op => op == AuditOperation.Insert));
    }

    // ── Sequential strategy ───────────────────────────────────────────────────

    [TestMethod]
    public async Task InsertManyAsync_Sequential_AllRowsPersisted()
    {
        var widgets = MakeWidgets(5);

        await _repository.InsertManyAsync(widgets, strategy: InsertStrategy.Sequential);

        Assert.AreEqual(5, CountWidgets());
    }

    [TestMethod]
    public async Task InsertManyAsync_Sequential_AuditEntryPerRow()
    {
        var widgets = MakeWidgets(4);

        await _repository.InsertManyAsync(widgets, strategy: InsertStrategy.Sequential);

        Assert.AreEqual(4, CountAuditEntries());
    }

    [TestMethod]
    public async Task InsertManyAsync_Sequential_FailingRowPropagatesException()
    {
        // Pre-insert a widget to create a duplicate-key conflict.
        var conflict = new Widget { Label = "Existing" };
        await _repository.InsertAsync(conflict);

        // Build a batch where the second entry duplicates the first's ID.
        var batch = new List<Widget>
        {
            new() { Label = "First" },
            new() { Id = conflict.Id, Label = "Duplicate" },
            new() { Label = "Third" }
        };

        // Sequential mode must propagate the constraint violation.
        await Assert.ThrowsExactlyAsync<SqliteException>(async () =>
            await _repository.InsertManyAsync(batch, strategy: InsertStrategy.Sequential));

        // The whole operation rolled back — only the pre-seeded conflict row remains.
        Assert.AreEqual(1, CountWidgets());
    }

    // ── UnitOfWork pass-through ───────────────────────────────────────────────

    [TestMethod]
    public async Task InsertManyAsync_WithUow_JoinsTransaction()
    {
        var widgets = MakeWidgets(3);

        await using var uow = new SqliteUnitOfWork(_factory);
        await uow.BeginTransactionAsync();
        await _repository.InsertManyAsync(widgets, uow);

        // Not yet committed — must not be visible on another connection.
        Assert.AreEqual(0, CountWidgets(), "Rows must not be visible before commit");

        await uow.CommitAsync();

        Assert.AreEqual(3, CountWidgets(), "Rows must be visible after commit");
    }

    // ── IAuditWriter bulk overload ────────────────────────────────────────────

    [TestMethod]
    public async Task AuditWriter_WriteAsync_BulkEntries_AllPersisted()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var tx = conn.BeginTransaction();

        var entries = Enumerable.Range(1, 5).Select(_ => new AuditEntry
        {
            TableName   = "Widgets",
            RecordId    = Guid.NewGuid().ToString("D").ToUpperInvariant(),
            Operation   = AuditOperation.Insert,
            PerformedAt = DateTime.UtcNow,
        }).ToList();

        await _auditWriter.WriteAsync(entries, conn, tx);
        tx.Commit();

        Assert.AreEqual(5, CountAuditEntries());
    }
}

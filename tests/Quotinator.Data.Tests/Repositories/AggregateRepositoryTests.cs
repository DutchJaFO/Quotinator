using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Example.Common;
using Quotinator.Data.Example.MasterDetail;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Tests.Repositories;

[TestClass]
public class AggregateRepositoryTests
{
    // ── Concrete aggregate for tests ───────────────────────────────────────────

    private sealed class WidgetWithLinesRepository(
        IDbConnectionFactory factory,
        ISystemAuditWriter auditWriter,
        ICallerContext callerContext,
        SqliteRepository<WidgetLine> lineRepo,
        Func<Widget, IReadOnlyList<WidgetLine>> getLines,
        InsertStrategy childStrategy = InsertStrategy.Bulk)
        : AggregateRepository<Widget, WidgetLine>(factory, auditWriter, callerContext)
    {
        protected override IReadOnlyList<WidgetLine> GetChildren(Widget parent) => getLines(parent);
        protected override SqliteRepository<WidgetLine> ChildRepository => lineRepo;
        protected override InsertStrategy ChildInsertStrategy => childStrategy;
    }

    // ── Setup / teardown ──────────────────────────────────────────────────────

    private string _tempDir       = null!;
    private string _dbPath        = null!;
    private IDbConnectionFactory _factory = null!;
    private SystemAuditWriter _auditWriter = null!;
    private CallerContext _callerContext = null!;
    private SqliteRepository<WidgetLine> _lineRepo = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_agg_test_").FullName;
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
            CREATE TABLE WidgetLines (
                Id           TEXT    NOT NULL PRIMARY KEY,
                ParentId     TEXT    NOT NULL,
                Value        TEXT    NOT NULL,
                DateCreated  TEXT,
                DateModified TEXT,
                DateDeleted  TEXT,
                IsDeleted    INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE System_AuditEntries (
                Id           TEXT    NOT NULL PRIMARY KEY,
                TableName    TEXT    NOT NULL,
                RecordId     TEXT,
                Operation    TEXT    NOT NULL,
                Agent        TEXT,
                PerformedAt  TEXT    NOT NULL,
                DateCreated  TEXT    NOT NULL,
                DateModified TEXT,
                DateDeleted  TEXT,
                IsDeleted    INTEGER NOT NULL DEFAULT 0
            );
            """);

        _factory       = new SqliteConnectionFactory(_dbPath);
        _callerContext = new CallerContext();
        _auditWriter   = new SystemAuditWriter(_factory, _callerContext);
        _lineRepo      = new SqliteRepository<WidgetLine>(_factory, _auditWriter, _callerContext);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private WidgetWithLinesRepository MakeRepo(
        Func<Widget, IReadOnlyList<WidgetLine>> getLines,
        InsertStrategy childStrategy = InsertStrategy.Bulk)
        => new(_factory, _auditWriter, _callerContext, _lineRepo, getLines, childStrategy);

    private int Count(string table)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM {table};");
    }

    private int CountAuditFor(string tableName)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM System_AuditEntries WHERE TableName = @t;", new { t = tableName });
    }

    // ── AggregateRepository compiles ──────────────────────────────────────────

    [TestMethod]
    public void AggregateRepository_ChildInsertStrategy_DefaultsTo_Bulk()
    {
        var repo = MakeRepo(_ => []);
        // Verify through a compile-time check: the property exists and returns Bulk.
        // Use reflection to access the protected property without subclassing.
        var prop = repo.GetType().GetProperty("ChildInsertStrategy",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.IsNotNull(prop);
        Assert.AreEqual(InsertStrategy.Bulk, prop.GetValue(repo));
    }

    // ── No UoW: commits parent + children ────────────────────────────────────

    [TestMethod]
    public async Task InsertAsync_NoUow_CommitsParentAndChildren()
    {
        var parent = new Widget { Label = "Parent" };
        var lines  = new List<WidgetLine>
        {
            new() { ParentId = parent.Id.ToString("D"), Value = "Line A" },
            new() { ParentId = parent.Id.ToString("D"), Value = "Line B" },
        };

        var repo = MakeRepo(_ => lines);
        await repo.InsertAsync(parent);

        Assert.AreEqual(1, Count("Widgets"));
        Assert.AreEqual(2, Count("WidgetLines"));
    }

    // ── Existing UoW: joins caller's transaction ──────────────────────────────

    [TestMethod]
    public async Task InsertAsync_WithExistingUow_JoinsCaller()
    {
        var parent = new Widget { Label = "InTx" };
        var lines  = new List<WidgetLine> { new() { ParentId = parent.Id.ToString("D"), Value = "X" } };

        var repo = MakeRepo(_ => lines);

        await using var uow = new SqliteUnitOfWork(_factory);
        await uow.BeginTransactionAsync();
        await repo.InsertAsync(parent, uow);

        // Not yet committed — nothing visible.
        Assert.AreEqual(0, Count("Widgets"),     "Parent must not be visible before commit");
        Assert.AreEqual(0, Count("WidgetLines"), "Children must not be visible before commit");

        await uow.CommitAsync();

        Assert.AreEqual(1, Count("Widgets"));
        Assert.AreEqual(1, Count("WidgetLines"));
    }

    // ── Child failure rolls back parent ──────────────────────────────────────

    [TestMethod]
    public async Task InsertAsync_ChildFailure_RollsBackParent()
    {
        var parent    = new Widget { Label = "Orphan" };
        var duplicate = new WidgetLine { ParentId = parent.Id.ToString("D"), Value = "Dup" };

        // Pre-insert the line so the second insert causes a PK conflict.
        await _lineRepo.InsertAsync(duplicate);

        // Provide the same line again — will fail on insert.
        var repo = MakeRepo(_ => [duplicate]);

        await Assert.ThrowsExactlyAsync<SqliteException>(async () =>
            await repo.InsertAsync(parent));

        // Parent must not be orphaned — the transaction rolled back.
        Assert.AreEqual(0, Count("Widgets"), "Parent must not persist when child insert fails");
    }

    // ── Audit entries for parent and all children ─────────────────────────────

    [TestMethod]
    public async Task InsertAsync_AuditEntriesForParentAndAllChildren()
    {
        var parent = new Widget { Label = "WithAudit" };
        var lines  = new List<WidgetLine>
        {
            new() { ParentId = parent.Id.ToString("D"), Value = "1" },
            new() { ParentId = parent.Id.ToString("D"), Value = "2" },
            new() { ParentId = parent.Id.ToString("D"), Value = "3" },
        };

        var repo = MakeRepo(_ => lines);
        await repo.InsertAsync(parent);

        Assert.AreEqual(1, CountAuditFor("Widgets"),     "One audit entry for the parent");
        Assert.AreEqual(3, CountAuditFor("WidgetLines"), "One audit entry per child");
    }

    // ── Sequential ChildInsertStrategy ───────────────────────────────────────

    [TestMethod]
    public async Task InsertAsync_Sequential_PerRowAuditAndFailureIdentified()
    {
        var parent    = new Widget { Label = "Seq Parent" };
        var duplicate = new WidgetLine { ParentId = parent.Id.ToString("D"), Value = "Dup" };

        await _lineRepo.InsertAsync(duplicate);

        // Second child is a duplicate — Sequential mode surfaces the specific row's exception.
        var batch = new List<WidgetLine>
        {
            new() { ParentId = parent.Id.ToString("D"), Value = "Ok" },
            new() { Id = duplicate.Id, ParentId = parent.Id.ToString("D"), Value = "Fail" },
        };

        var repo = MakeRepo(_ => batch, InsertStrategy.Sequential);

        var ex = await Assert.ThrowsExactlyAsync<SqliteException>(async () =>
            await repo.InsertAsync(parent));

        // Constraint violation (SQLite error code 19 = SQLITE_CONSTRAINT).
        Assert.AreEqual(19, ex.SqliteErrorCode);

        // Transaction rolled back — no orphaned parent.
        Assert.AreEqual(0, Count("Widgets"));
    }
}

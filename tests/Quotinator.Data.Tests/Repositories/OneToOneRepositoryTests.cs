using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Example.Common;
using Quotinator.Data.Example.OneToOne;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Tests.Repositories;

[TestClass]
public class OneToOneRepositoryTests
{
    // ── Setup / teardown ──────────────────────────────────────────────────────

    private string _tempDir       = null!;
    private string _dbPath        = null!;
    private IDbConnectionFactory _factory  = null!;
    private SystemAuditWriter _auditWriter = null!;
    private CallerContext _callerContext   = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_1to1_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute("""
            CREATE TABLE Widgets (
                Id           TEXT NOT NULL PRIMARY KEY,
                Label        TEXT NOT NULL,
                DateCreated  TEXT,
                DateModified TEXT,
                DateDeleted  TEXT,
                IsDeleted    INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE WidgetDetails (
                Id           TEXT NOT NULL PRIMARY KEY REFERENCES Widgets(Id),
                Notes        TEXT NOT NULL,
                DateCreated  TEXT,
                DateModified TEXT,
                DateDeleted  TEXT,
                IsDeleted    INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE WidgetDetailsFk (
                Id           TEXT NOT NULL PRIMARY KEY,
                WidgetId     TEXT REFERENCES Widgets(Id),
                Notes        TEXT NOT NULL,
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
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private WidgetWithDetailRepository   MakeSharedPkRepo()
        => new(_factory, _auditWriter, _callerContext);

    private WidgetWithFkDetailRepository MakeSeparateFkRepo()
        => new(_factory, _auditWriter, _callerContext);

    private int Count(string table)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM {table} WHERE IsDeleted = 0;");
    }

    private int CountAuditFor(string table)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM System_AuditEntries WHERE TableName = @t;", new { t = table });
    }

    // ── Shared PK ─────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task SharedPk_Insert_BothRowsCommitted()
    {
        var repo   = MakeSharedPkRepo();
        var parent = new Widget { Label = "SharedPkParent" };

        await repo.InsertAsync(parent);

        Assert.AreEqual(1, Count("Widgets"));
        Assert.AreEqual(1, Count("WidgetDetails"));
    }

    [TestMethod]
    public async Task SharedPk_Insert_AuditEntriesForBoth()
    {
        var repo   = MakeSharedPkRepo();
        var parent = new Widget { Label = "AuditSharedPk" };

        await repo.InsertAsync(parent);

        Assert.AreEqual(1, CountAuditFor("Widgets"),      "One audit entry for the parent");
        Assert.AreEqual(1, CountAuditFor("WidgetDetails"), "One audit entry for the detail");
    }

    [TestMethod]
    public async Task SharedPk_Insert_Rollback_NeitherRowPersists()
    {
        var repo   = MakeSharedPkRepo();
        var parent = new Widget { Label = "RollbackSharedPk" };

        await using var uow = new SqliteUnitOfWork(_factory);
        await uow.BeginTransactionAsync();
        await repo.InsertAsync(parent, uow);
        await uow.RollbackAsync();

        Assert.AreEqual(0, Count("Widgets"),      "Parent must not persist after rollback");
        Assert.AreEqual(0, Count("WidgetDetails"), "Detail must not persist after rollback");
    }

    [TestMethod]
    public async Task SharedPk_GetDetailAsync_ReturnsDetail()
    {
        var repo   = MakeSharedPkRepo();
        var parent = new Widget { Label = "GetDetail" };
        await repo.InsertAsync(parent);

        var detail = await repo.GetDetailAsync(parent.Id);

        Assert.IsNotNull(detail);
        Assert.AreEqual(parent.Id, detail.Id);
    }

    [TestMethod]
    public async Task SharedPk_GetDetailAsync_ReturnsNull_WhenNoDetail()
    {
        var repo   = MakeSharedPkRepo();
        var detail = await repo.GetDetailAsync(Guid.NewGuid());

        Assert.IsNull(detail);
    }

    // ── Separate FK ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task SeparateFk_Insert_BothRowsCommitted()
    {
        var repo   = MakeSeparateFkRepo();
        var parent = new Widget { Label = "FkParent" };

        await repo.InsertAsync(parent);

        Assert.AreEqual(1, Count("Widgets"));
        Assert.AreEqual(1, Count("WidgetDetailsFk"));
    }

    [TestMethod]
    public async Task SeparateFk_Insert_AuditEntriesForBoth()
    {
        var repo   = MakeSeparateFkRepo();
        var parent = new Widget { Label = "AuditFk" };

        await repo.InsertAsync(parent);

        Assert.AreEqual(1, CountAuditFor("Widgets"),        "One audit entry for the parent");
        Assert.AreEqual(1, CountAuditFor("WidgetDetailsFk"), "One audit entry for the detail");
    }

    [TestMethod]
    public async Task SeparateFk_Insert_Rollback_NeitherRowPersists()
    {
        var repo   = MakeSeparateFkRepo();
        var parent = new Widget { Label = "RollbackFk" };

        await using var uow = new SqliteUnitOfWork(_factory);
        await uow.BeginTransactionAsync();
        await repo.InsertAsync(parent, uow);
        await uow.RollbackAsync();

        Assert.AreEqual(0, Count("Widgets"),        "Parent must not persist after rollback");
        Assert.AreEqual(0, Count("WidgetDetailsFk"), "Detail must not persist after rollback");
    }

    [TestMethod]
    public async Task SeparateFk_GetDetailAsync_ReturnsDetail()
    {
        var repo   = MakeSeparateFkRepo();
        var parent = new Widget { Label = "FkGetDetail" };
        await repo.InsertAsync(parent);

        var detail = await repo.GetDetailAsync(parent.Id);

        Assert.IsNotNull(detail);
        Assert.AreEqual(parent.Id.ToString("D").ToUpperInvariant(), detail.WidgetId);
    }

    [TestMethod]
    public async Task SeparateFk_GetDetailAsync_ReturnsNull_WhenNoDetail()
    {
        var repo   = MakeSeparateFkRepo();
        var detail = await repo.GetDetailAsync(Guid.NewGuid());

        Assert.IsNull(detail);
    }
}

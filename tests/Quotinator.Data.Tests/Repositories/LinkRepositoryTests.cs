using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Example.Common;
using Quotinator.Data.Example.ManyToMany;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Tests.Repositories;

[TestClass]
public class LinkRepositoryTests
{
    // ── Setup / teardown ──────────────────────────────────────────────────────

    private string _tempDir         = null!;
    private string _dbPath          = null!;
    private IDbConnectionFactory _factory = null!;
    private SystemAuditWriter _auditWriter = null!;
    private CallerContext _callerContext = null!;
    private WidgetTagLinkRepository _repo = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_m2m_test_").FullName;
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
            CREATE TABLE Tags (
                Id           TEXT NOT NULL PRIMARY KEY,
                Name         TEXT NOT NULL,
                DateCreated  TEXT,
                DateModified TEXT,
                DateDeleted  TEXT,
                IsDeleted    INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE WidgetTags (
                Id           TEXT NOT NULL PRIMARY KEY,
                WidgetId     TEXT NOT NULL REFERENCES Widgets(Id),
                TagId        TEXT NOT NULL REFERENCES Tags(Id),
                DateCreated  TEXT,
                DateModified TEXT,
                DateDeleted  TEXT,
                IsDeleted    INTEGER NOT NULL DEFAULT 0,
                UNIQUE (WidgetId, TagId)
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
        _repo          = new WidgetTagLinkRepository(_factory, _auditWriter, _callerContext);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Seed helpers ──────────────────────────────────────────────────────────

    private async Task<Widget> SeedWidgetAsync(string label = "Widget")
    {
        var widget = new Widget { Label = label };
        var widgetRepo = new SqliteRepository<Widget>(_factory, _auditWriter, _callerContext);
        await widgetRepo.InsertAsync(widget);
        return widget;
    }

    private async Task<Tag> SeedTagAsync(string name = "Tag")
    {
        var tag = new Tag { Name = name };
        var tagRepo = new SqliteRepository<Tag>(_factory, _auditWriter, _callerContext);
        await tagRepo.InsertAsync(tag);
        return tag;
    }

    private int CountJunctionRows(bool activeOnly = true)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var filter = activeOnly ? "WHERE IsDeleted = 0" : string.Empty;
        return conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM WidgetTags {filter};");
    }

    private int CountAuditFor(string tableName)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM System_AuditEntries WHERE TableName = @t;", new { t = tableName });
    }

    // ── LinkAsync ─────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LinkAsync_InsertsJunctionRow()
    {
        var widget = await SeedWidgetAsync();
        var tag    = await SeedTagAsync();

        await _repo.LinkAsync(widget.Id, tag.Id);

        Assert.AreEqual(1, CountJunctionRows());
    }

    [TestMethod]
    public async Task LinkAsync_NewLink_WritesInsertAuditEntry()
    {
        var widget = await SeedWidgetAsync();
        var tag    = await SeedTagAsync();

        await _repo.LinkAsync(widget.Id, tag.Id);

        Assert.AreEqual(1, CountAuditFor("WidgetTags"));
    }

    [TestMethod]
    public async Task LinkAsync_Restore_WritesRestoreAuditEntry()
    {
        var widget = await SeedWidgetAsync();
        var tag    = await SeedTagAsync();

        await _repo.LinkAsync(widget.Id, tag.Id);   // Insert → 1
        await _repo.UnlinkAsync(widget.Id, tag.Id); // SoftDelete → 2
        await _repo.LinkAsync(widget.Id, tag.Id);   // Restore → 3

        Assert.AreEqual(3, CountAuditFor("WidgetTags"));
    }

    [TestMethod]
    public async Task LinkAsync_IsIdempotent_WhenAlreadyLinked()
    {
        var widget = await SeedWidgetAsync();
        var tag    = await SeedTagAsync();

        await _repo.LinkAsync(widget.Id, tag.Id);
        await _repo.LinkAsync(widget.Id, tag.Id); // second call must not throw or duplicate

        Assert.AreEqual(1, CountJunctionRows());
    }

    [TestMethod]
    public async Task LinkAsync_RestoresSoftDeletedRow()
    {
        var widget = await SeedWidgetAsync();
        var tag    = await SeedTagAsync();

        await _repo.LinkAsync(widget.Id, tag.Id);
        await _repo.UnlinkAsync(widget.Id, tag.Id);
        await _repo.LinkAsync(widget.Id, tag.Id); // must restore, not insert a second row

        Assert.AreEqual(1, CountJunctionRows(activeOnly: true));
        Assert.AreEqual(1, CountJunctionRows(activeOnly: false)); // still only one physical row
    }

    // ── UnlinkAsync ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task UnlinkAsync_SoftDeletesJunctionRow()
    {
        var widget = await SeedWidgetAsync();
        var tag    = await SeedTagAsync();

        await _repo.LinkAsync(widget.Id, tag.Id);
        await _repo.UnlinkAsync(widget.Id, tag.Id);

        Assert.AreEqual(0, CountJunctionRows(activeOnly: true));
        Assert.AreEqual(1, CountJunctionRows(activeOnly: false)); // row still exists, just deleted
    }

    [TestMethod]
    public async Task UnlinkAsync_WritesSoftDeleteAuditEntry()
    {
        var widget = await SeedWidgetAsync();
        var tag    = await SeedTagAsync();

        await _repo.LinkAsync(widget.Id, tag.Id);   // Insert → 1
        await _repo.UnlinkAsync(widget.Id, tag.Id); // SoftDelete → 2

        Assert.AreEqual(2, CountAuditFor("WidgetTags"));
    }

    [TestMethod]
    public async Task UnlinkAsync_IsNoOp_WhenNotLinked()
    {
        var widget = await SeedWidgetAsync();
        var tag    = await SeedTagAsync();

        await _repo.UnlinkAsync(widget.Id, tag.Id); // no exception expected

        Assert.AreEqual(0, CountJunctionRows());
    }

    // ── RestoreLinkAsync ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task RestoreLinkAsync_RestoresSoftDeletedRow()
    {
        var widget = await SeedWidgetAsync();
        var tag    = await SeedTagAsync();

        await _repo.LinkAsync(widget.Id, tag.Id);
        await _repo.UnlinkAsync(widget.Id, tag.Id);
        await _repo.RestoreLinkAsync(widget.Id, tag.Id);

        Assert.AreEqual(1, CountJunctionRows(activeOnly: true));
    }

    [TestMethod]
    public async Task RestoreLinkAsync_WritesRestoreAuditEntry()
    {
        var widget = await SeedWidgetAsync();
        var tag    = await SeedTagAsync();

        await _repo.LinkAsync(widget.Id, tag.Id);        // Insert → 1
        await _repo.UnlinkAsync(widget.Id, tag.Id);      // SoftDelete → 2
        await _repo.RestoreLinkAsync(widget.Id, tag.Id); // Restore → 3

        Assert.AreEqual(3, CountAuditFor("WidgetTags"));
    }

    [TestMethod]
    public async Task RestoreLinkAsync_IsNoOp_WhenRowNotSoftDeleted()
    {
        var widget = await SeedWidgetAsync();
        var tag    = await SeedTagAsync();

        await _repo.LinkAsync(widget.Id, tag.Id);
        await _repo.RestoreLinkAsync(widget.Id, tag.Id); // already active — no exception

        Assert.AreEqual(1, CountJunctionRows(activeOnly: true));
    }

    // ── GetRightAsync ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetRightAsync_ReturnsLinkedTags()
    {
        var widget = await SeedWidgetAsync();
        var tagA   = await SeedTagAsync("A");
        var tagB   = await SeedTagAsync("B");

        await _repo.LinkAsync(widget.Id, tagA.Id);
        await _repo.LinkAsync(widget.Id, tagB.Id);

        var tags = await _repo.GetRightAsync(widget.Id);

        Assert.AreEqual(2, tags.Count);
        Assert.IsTrue(tags.Any(t => t.Id == tagA.Id));
        Assert.IsTrue(tags.Any(t => t.Id == tagB.Id));
    }

    [TestMethod]
    public async Task GetRightAsync_ExcludesSoftDeletedLinks()
    {
        var widget = await SeedWidgetAsync();
        var tagA   = await SeedTagAsync("A");
        var tagB   = await SeedTagAsync("B");

        await _repo.LinkAsync(widget.Id, tagA.Id);
        await _repo.LinkAsync(widget.Id, tagB.Id);
        await _repo.UnlinkAsync(widget.Id, tagA.Id);

        var tags = await _repo.GetRightAsync(widget.Id);

        Assert.AreEqual(1, tags.Count);
        Assert.AreEqual(tagB.Id, tags[0].Id);
    }

    [TestMethod]
    public async Task GetRightAsync_ReturnsEmpty_WhenNoLinks()
    {
        var widget = await SeedWidgetAsync();

        var tags = await _repo.GetRightAsync(widget.Id);

        Assert.AreEqual(0, tags.Count);
    }

    // ── GetLeftAsync ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetLeftAsync_ReturnsLinkedWidgets()
    {
        var widgetA = await SeedWidgetAsync("A");
        var widgetB = await SeedWidgetAsync("B");
        var tag     = await SeedTagAsync();

        await _repo.LinkAsync(widgetA.Id, tag.Id);
        await _repo.LinkAsync(widgetB.Id, tag.Id);

        var widgets = await _repo.GetLeftAsync(tag.Id);

        Assert.AreEqual(2, widgets.Count);
        Assert.IsTrue(widgets.Any(w => w.Id == widgetA.Id));
        Assert.IsTrue(widgets.Any(w => w.Id == widgetB.Id));
    }

    [TestMethod]
    public async Task GetLeftAsync_ExcludesSoftDeletedLinks()
    {
        var widgetA = await SeedWidgetAsync("A");
        var widgetB = await SeedWidgetAsync("B");
        var tag     = await SeedTagAsync();

        await _repo.LinkAsync(widgetA.Id, tag.Id);
        await _repo.LinkAsync(widgetB.Id, tag.Id);
        await _repo.UnlinkAsync(widgetA.Id, tag.Id);

        var widgets = await _repo.GetLeftAsync(tag.Id);

        Assert.AreEqual(1, widgets.Count);
        Assert.AreEqual(widgetB.Id, widgets[0].Id);
    }

    [TestMethod]
    public async Task GetLeftAsync_ReturnsEmpty_WhenNoLinks()
    {
        var tag = await SeedTagAsync();

        var widgets = await _repo.GetLeftAsync(tag.Id);

        Assert.AreEqual(0, widgets.Count);
    }

    // ── UnitOfWork integration ────────────────────────────────────────────────

    [TestMethod]
    public async Task LinkAsync_WithUoW_RollsBackOnAbort()
    {
        var widget = await SeedWidgetAsync();
        var tag    = await SeedTagAsync();

        await using var uow = new SqliteUnitOfWork(_factory);
        await uow.BeginTransactionAsync();
        await _repo.LinkAsync(widget.Id, tag.Id, uow);
        await uow.RollbackAsync();

        Assert.AreEqual(0, CountJunctionRows());
    }

    [TestMethod]
    public async Task LinkAsync_WithUoW_CommitsOnSuccess()
    {
        var widget = await SeedWidgetAsync();
        var tag    = await SeedTagAsync();

        await using var uow = new SqliteUnitOfWork(_factory);
        await uow.BeginTransactionAsync();
        await _repo.LinkAsync(widget.Id, tag.Id, uow);
        await uow.CommitAsync();

        Assert.AreEqual(1, CountJunctionRows());
    }
}

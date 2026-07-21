using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Example.Common;
using Quotinator.Data.Repositories;
using Quotinator.Data.Tests.Helpers;

namespace Quotinator.Data.Tests.Repositories;

[TestClass]
public class SystemAuditWriterTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;
    private IDbConnectionFactory _factory = null!;
    private CallerContext _caller = null!;
    private SystemAuditWriter _writer = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_audit_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute("""
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

        _factory = new SqliteConnectionFactory(_dbPath);
        _caller  = new CallerContext();
        _writer  = new SystemAuditWriter(_factory, _caller);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static SystemAuditEntry BuildEntry(string op, Guid? id = null, string? agent = null) => new()
    {
        TableName   = "Widgets",
        RecordId    = id?.ToString("D").ToUpperInvariant(),
        Operation   = op,
        Agent       = agent,
        PerformedAt = DateTime.UtcNow,
    };

    // ── WriteAsync (standalone) ──────────────────────────────────────────────

    [TestMethod]
    public async Task WriteAsync_StandaloneOverload_PersistsEntry()
    {
        var id    = Guid.NewGuid();
        var entry = BuildEntry(AuditOperation.Insert, id, "TestAgent/1.0");

        await _writer.WriteAsync(entry);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var count = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM System_AuditEntries;");
        Assert.AreEqual(1, count);
    }

    [TestMethod]
    public async Task WriteAsync_StandaloneOverload_FieldsAreCorrect()
    {
        var id    = Guid.NewGuid();
        var entry = BuildEntry(AuditOperation.Insert, id, "TestAgent/1.0");

        await _writer.WriteAsync(entry);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var row = conn.QuerySingle<SystemAuditEntry>("SELECT * FROM System_AuditEntries;");

        Assert.AreEqual("Widgets",                         row.TableName);
        Assert.AreEqual(id.ToString("D").ToUpperInvariant(), row.RecordId);
        Assert.AreEqual(AuditOperation.Insert,             row.Operation);
        Assert.AreEqual("TestAgent/1.0",                   row.Agent);
    }

    [TestMethod]
    public async Task WriteAsync_NullAgent_Persists()
    {
        await _writer.WriteAsync(BuildEntry(AuditOperation.Reseed, null, null));

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var row = conn.QuerySingle<SystemAuditEntry>("SELECT * FROM System_AuditEntries;");
        Assert.IsNull(row.Agent);
        Assert.IsNull(row.RecordId);
    }

    // ── WriteAsync (connection overload) ────────────────────────────────────

    [TestMethod]
    public async Task WriteAsync_ConnectionOverload_PersistsInSameTransaction()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var tx = conn.BeginTransaction();

        var entry = BuildEntry(AuditOperation.Update, Guid.NewGuid());
        await _writer.WriteAsync(entry, conn, tx);

        // Not committed yet — should not be visible on a separate connection.
        using var conn2  = new SqliteConnection($"Data Source={_dbPath}");
        conn2.Open();
        var countBefore = conn2.ExecuteScalar<int>("SELECT COUNT(*) FROM System_AuditEntries;");
        Assert.AreEqual(0, countBefore, "Entry must not be visible before commit");

        tx.Commit();

        var countAfter = conn2.ExecuteScalar<int>("SELECT COUNT(*) FROM System_AuditEntries;");
        Assert.AreEqual(1, countAfter, "Entry must be visible after commit");
    }

    // ── CallerContext ────────────────────────────────────────────────────────

    [TestMethod]
    public void CallerContext_SetAgent_IsIsolatedPerTask()
    {
        _caller.Agent = "task-A";

        var agentInTask = string.Empty;
        Task.Run(() =>
        {
            _caller.Agent = "task-B";
            agentInTask   = _caller.Agent;
        }).Wait();

        // The original context must still see "task-A".
        Assert.AreEqual("task-A", _caller.Agent);
        Assert.AreEqual("task-B", agentInTask);
    }

    // ── ClearAsync ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ClearAsync_NoFilter_DeletesAllEntriesAndWritesPurgeEntry()
    {
        // Seed two entries for different tables.
        await _writer.WriteAsync(BuildEntry(AuditOperation.Insert));
        await _writer.WriteAsync(new SystemAuditEntry { TableName = "Sources", Operation = AuditOperation.Insert, PerformedAt = DateTime.UtcNow });

        _caller.Agent = "Cleaner/1.0";
        await _writer.ClearAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        // Exactly one entry should remain — the purge record itself.
        var remaining = conn.Query<SystemAuditEntry>("SELECT * FROM System_AuditEntries;").ToList();
        Assert.AreEqual(1, remaining.Count, "Only the purge sentinel entry should remain");
        Assert.AreEqual("System_AuditEntries", remaining[0].TableName);
        Assert.AreEqual(AuditOperation.Purge, remaining[0].Operation);
        Assert.AreEqual("Cleaner/1.0",     remaining[0].Agent);
    }

    [TestMethod]
    public async Task ClearAsync_WithTable_DeletesOnlyMatchingTableEntriesAndWritesPurgeEntry()
    {
        await _writer.WriteAsync(BuildEntry(AuditOperation.Insert));
        await _writer.WriteAsync(new SystemAuditEntry { TableName = "Sources", Operation = AuditOperation.Insert, PerformedAt = DateTime.UtcNow });

        await _writer.ClearAsync("Widgets");

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var remaining = conn.Query<SystemAuditEntry>("SELECT * FROM System_AuditEntries;").ToList();

        // The Sources entry must be untouched; the Widgets entry deleted; purge sentinel added.
        // Looked up by TableName rather than insertion order — Id is now a random Guid (RecordBase),
        // not an auto-increment integer, so it no longer reflects write order.
        Assert.AreEqual(2, remaining.Count);
        var sourcesEntry = remaining.Single(r => r.TableName == "Sources");
        var widgetsEntry = remaining.Single(r => r.TableName == "Widgets");
        Assert.AreEqual(AuditOperation.Insert, sourcesEntry.Operation, "Sources entry must be untouched");
        Assert.AreEqual(AuditOperation.Purge, widgetsEntry.Operation, "Purge entry TableName must be the cleared table");
    }

    // ── Repository audit writes ──────────────────────────────────────────────

    [TestMethod]
    public async Task SqliteRepository_Insert_WritesAuditEntry()
    {
        // Set up a Widgets table and wire up the real SystemAuditWriter.
        using var setup = new SqliteConnection($"Data Source={_dbPath}");
        setup.Open();
        setup.Execute("""
            CREATE TABLE Widgets (
                Id           TEXT    NOT NULL PRIMARY KEY,
                Label        TEXT    NOT NULL,
                DateCreated  TEXT,
                DateModified TEXT,
                DateDeleted  TEXT,
                IsDeleted    INTEGER NOT NULL DEFAULT 0
            );
            """);

        var repo = new SqliteRepository<Widget>(_factory, _writer, _caller);
        var w    = new Widget { Id = Guid.NewGuid(), Label = "hello" };
        await repo.InsertAsync(w);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var entry = conn.QuerySingle<SystemAuditEntry>("SELECT * FROM System_AuditEntries;");

        Assert.AreEqual("Widgets",          entry.TableName);
        Assert.AreEqual(AuditOperation.Insert, entry.Operation);
        Assert.AreEqual(w.Id.ToString("D"), entry.RecordId);
    }
}

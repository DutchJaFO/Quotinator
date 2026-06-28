using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Example.Common;
using Quotinator.Data.Repositories;
using Quotinator.Data.Tests.Helpers;

namespace Quotinator.Data.Tests.Repositories;

[TestClass]
public class AuditWriterTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;
    private IDbConnectionFactory _factory = null!;
    private CallerContext _caller = null!;
    private AuditWriter   _writer = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_audit_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute("""
            CREATE TABLE AuditEntries (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                TableName   TEXT    NOT NULL,
                RecordId    TEXT,
                Operation   TEXT    NOT NULL,
                Agent       TEXT,
                PerformedAt TEXT    NOT NULL
            );
            """);

        _factory = new SqliteConnectionFactory(_dbPath);
        _caller  = new CallerContext();
        _writer  = new AuditWriter(_factory, _caller);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static AuditEntry BuildEntry(string op, Guid? id = null, string? agent = null) => new()
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
        var count = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM AuditEntries;");
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
        var row = conn.QuerySingle<AuditEntry>("SELECT * FROM AuditEntries;");

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
        var row = conn.QuerySingle<AuditEntry>("SELECT * FROM AuditEntries;");
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
        var countBefore = conn2.ExecuteScalar<int>("SELECT COUNT(*) FROM AuditEntries;");
        Assert.AreEqual(0, countBefore, "Entry must not be visible before commit");

        tx.Commit();

        var countAfter = conn2.ExecuteScalar<int>("SELECT COUNT(*) FROM AuditEntries;");
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
        await _writer.WriteAsync(new AuditEntry { TableName = "Sources", Operation = AuditOperation.Insert, PerformedAt = DateTime.UtcNow });

        _caller.Agent = "Cleaner/1.0";
        await _writer.ClearAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        // Exactly one entry should remain — the purge record itself.
        var remaining = conn.Query<AuditEntry>("SELECT * FROM AuditEntries;").ToList();
        Assert.AreEqual(1, remaining.Count, "Only the purge sentinel entry should remain");
        Assert.AreEqual("AuditEntries",    remaining[0].TableName);
        Assert.AreEqual(AuditOperation.Purge, remaining[0].Operation);
        Assert.AreEqual("Cleaner/1.0",     remaining[0].Agent);
    }

    [TestMethod]
    public async Task ClearAsync_WithTable_DeletesOnlyMatchingTableEntriesAndWritesPurgeEntry()
    {
        await _writer.WriteAsync(BuildEntry(AuditOperation.Insert));
        await _writer.WriteAsync(new AuditEntry { TableName = "Sources", Operation = AuditOperation.Insert, PerformedAt = DateTime.UtcNow });

        await _writer.ClearAsync("Widgets");

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var remaining = conn.Query<AuditEntry>("SELECT * FROM AuditEntries ORDER BY Id;").ToList();

        // The Sources entry must be untouched; the Widgets entry deleted; purge sentinel added.
        Assert.AreEqual(2,           remaining.Count);
        Assert.AreEqual("Sources",   remaining[0].TableName);
        Assert.AreEqual("Widgets",   remaining[1].TableName, "Purge entry TableName must be the cleared table");
        Assert.AreEqual(AuditOperation.Purge, remaining[1].Operation);
    }

    // ── Repository audit writes ──────────────────────────────────────────────

    [TestMethod]
    public async Task SqliteRepository_Insert_WritesAuditEntry()
    {
        // Set up a Widgets table and wire up the real AuditWriter.
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
        var entry = conn.QuerySingle<AuditEntry>("SELECT * FROM AuditEntries;");

        Assert.AreEqual("Widgets",          entry.TableName);
        Assert.AreEqual(AuditOperation.Insert, entry.Operation);
        Assert.AreEqual(w.Id.ToString("D").ToUpperInvariant(), entry.RecordId);
    }
}

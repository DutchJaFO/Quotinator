using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Tests.Repositories;

[TestClass]
public class SystemImportConflictWriterReaderTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;
    private IDbConnectionFactory _factory = null!;
    private SystemImportConflictWriter _writer = null!;
    private SystemImportConflictReader _reader = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_conflict_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute("""
            CREATE TABLE System_ImportConflicts (
                Id              TEXT    NOT NULL PRIMARY KEY,
                BatchId         TEXT    NOT NULL,
                ExistingBatchId TEXT,
                EntityType      TEXT    NOT NULL,
                EntityId        TEXT,
                ExistingValue   TEXT,
                IncomingValue   TEXT,
                AppliedPolicy   TEXT,
                Status          TEXT    NOT NULL,
                MergedFields    TEXT,
                DetectedAt      TEXT    NOT NULL,
                ResolvedAt      TEXT,
                DateCreated     TEXT    NOT NULL,
                DateModified    TEXT,
                DateDeleted     TEXT,
                IsDeleted       INTEGER NOT NULL DEFAULT 0
            );
            """);

        _factory = new SqliteConnectionFactory(_dbPath);
        _writer  = new SystemImportConflictWriter(_factory);
        _reader  = new SystemImportConflictReader(_factory);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static SystemImportConflict BuildPendingConflict(string batchId, string? existingBatchId = null) => new()
    {
        BatchId         = batchId,
        ExistingBatchId = existingBatchId,
        EntityType      = "Quote",
        EntityId        = Guid.NewGuid().ToString(),
        ExistingValue   = "{}",
        IncomingValue   = "{}",
        Status          = ImportConflictStatus.Pending,
        DetectedAt      = DateTime.UtcNow,
    };

    [TestMethod]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        var result = await _reader.GetByIdAsync(Guid.NewGuid());
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetByIdAsync_ExistingConflict_ReturnsIt()
    {
        var entry = BuildPendingConflict("BATCH-1");
        await _writer.WriteAsync(entry);

        var found = await _reader.GetByIdAsync(entry.Id);

        Assert.IsNotNull(found);
        Assert.AreEqual(entry.Id, found!.Id);
        Assert.AreEqual(ImportConflictStatus.Pending, found.Status);
    }

    [TestMethod]
    public async Task GetAllForBatchAsync_ReturnsOnlyMatchingBatchRegardlessOfStatus()
    {
        var a1 = BuildPendingConflict("BATCH-A");
        var a2 = BuildPendingConflict("BATCH-A");
        var b1 = BuildPendingConflict("BATCH-B");
        await _writer.WriteAsync(a1);
        await _writer.WriteAsync(a2);
        await _writer.WriteAsync(b1);

        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            await _writer.MarkResolvedAsync(a2.Id, conn);
        }

        var batchA = await _reader.GetAllForBatchAsync("BATCH-A");

        Assert.AreEqual(2, batchA.Count);
        CollectionAssert.AreEquivalent(new[] { a1.Id, a2.Id }, batchA.Select(c => c.Id).ToList());
    }

    [TestMethod]
    public async Task MarkDecidedAsync_TransitionsToDecidedAndStoresDecisionJson()
    {
        var entry = BuildPendingConflict("BATCH-1");
        await _writer.WriteAsync(entry);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        await _writer.MarkDecidedAsync(entry.Id, """{"date":{"choice":"Replace"}}""", conn);

        var found = await _reader.GetByIdAsync(entry.Id);
        Assert.AreEqual(ImportConflictStatus.Decided, found!.Status);
        Assert.AreEqual("""{"date":{"choice":"Replace"}}""", found.MergedFields);
    }

    [TestMethod]
    public async Task MarkDecidedAsync_CalledAgain_OverwritesPriorDecision()
    {
        var entry = BuildPendingConflict("BATCH-1");
        await _writer.WriteAsync(entry);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        await _writer.MarkDecidedAsync(entry.Id, "\"first\"", conn);
        await _writer.MarkDecidedAsync(entry.Id, "\"second\"", conn);

        var found = await _reader.GetByIdAsync(entry.Id);
        Assert.AreEqual(ImportConflictStatus.Decided, found!.Status);
        Assert.AreEqual("\"second\"", found.MergedFields);
    }

    [TestMethod]
    public async Task ClearDecisionAsync_RevertsToPendingAndClearsMergedFields()
    {
        var entry = BuildPendingConflict("BATCH-1");
        await _writer.WriteAsync(entry);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        await _writer.MarkDecidedAsync(entry.Id, "\"decision\"", conn);
        await _writer.ClearDecisionAsync(entry.Id, conn);

        var found = await _reader.GetByIdAsync(entry.Id);
        Assert.AreEqual(ImportConflictStatus.Pending, found!.Status);
        Assert.IsNull(found.MergedFields);
    }

    [TestMethod]
    public async Task MarkResolvedAsync_SetsResolvedStatusAndResolvedAt()
    {
        var entry = BuildPendingConflict("BATCH-1");
        await _writer.WriteAsync(entry);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        await _writer.MarkResolvedAsync(entry.Id, conn);

        var found = await _reader.GetByIdAsync(entry.Id);
        Assert.AreEqual(ImportConflictStatus.Resolved, found!.Status);
        Assert.IsNotNull(found.ResolvedAt);
    }

    [TestMethod]
    public async Task ExistingBatchId_RoundTripsCorrectly()
    {
        var entry = BuildPendingConflict("BATCH-2", existingBatchId: "BATCH-1");
        await _writer.WriteAsync(entry);

        var found = await _reader.GetByIdAsync(entry.Id);

        Assert.AreEqual("BATCH-1", found!.ExistingBatchId);
        Assert.AreEqual("BATCH-2", found.BatchId);
    }
}

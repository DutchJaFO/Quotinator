using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Tests.Repositories;

[TestClass]
public class SystemImportActionWriterReaderTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;
    private IDbConnectionFactory _factory = null!;
    private SystemImportActionWriter _writer = null!;
    private SystemImportActionReader _reader = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_action_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute("""
            CREATE TABLE System_ImportActions (
                Id                 TEXT    NOT NULL PRIMARY KEY,
                BatchId            TEXT    NOT NULL,
                ActionType         TEXT    NOT NULL
                                   CHECK (ActionType IN ('Add', 'Modify')),
                EntityType         TEXT    NOT NULL,
                EntityId           TEXT    NOT NULL,
                ExistingBatchId    TEXT,
                ExistingValue      TEXT,
                IncomingValue      TEXT    NOT NULL,
                AppliedPolicy      TEXT,
                Status             TEXT    NOT NULL
                                   CHECK (Status IN ('Pending', 'Decided', 'Applied', 'Discarded', 'Blocked')),
                MergedFields       TEXT,
                MarkCompletenessAs TEXT
                                   CHECK (MarkCompletenessAs IS NULL OR MarkCompletenessAs IN ('Incomplete', 'NeedsReview', 'Complete')),
                DetectedAt         TEXT    NOT NULL,
                AppliedAt          TEXT,
                DiscardedAt        TEXT,
                DateCreated        TEXT    NOT NULL,
                DateModified       TEXT,
                DateDeleted        TEXT,
                IsDeleted          INTEGER NOT NULL DEFAULT 0
            );
            """);

        _factory = new SqliteConnectionFactory(_dbPath);
        _writer  = new SystemImportActionWriter(_factory);
        _reader  = new SystemImportActionReader(_factory);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static SystemImportAction BuildPendingModify(string batchId, string? existingBatchId = null) => new()
    {
        BatchId         = batchId,
        ExistingBatchId = existingBatchId,
        ActionType      = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
        EntityType      = "Quote",
        EntityId        = Guid.NewGuid().ToString(),
        ExistingValue   = "{}",
        IncomingValue   = "{}",
        Status          = new SafeValue<ImportActionStatus?>(ImportActionStatus.Pending.ToString(), ImportActionStatus.Pending),
        DetectedAt      = DateTime.UtcNow,
    };

    private static SystemImportAction BuildDecidedAdd(string batchId) => new()
    {
        BatchId    = batchId,
        ActionType = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
        EntityType = "Quote",
        EntityId   = Guid.NewGuid().ToString(),
        IncomingValue = "{}",
        Status     = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
        DetectedAt = DateTime.UtcNow,
    };

    // ── GetPagedAsync (#195) ──────────────────────────────────────────────────

    [TestMethod]
    public async Task GetPagedAsync_PageSizeZero_ReturnsEveryRowNotZeroRows()
    {
        for (var i = 0; i < 3; i++)
            await _writer.WriteAsync(BuildDecidedAdd("BATCH-1"));

        var result = await _reader.GetPagedAsync(null, null, null, 1, 0);

        Assert.AreEqual(3, result.Items.Count, "pageSize = 0 must reach SQLite as LIMIT -1, not a literal LIMIT 0");
        Assert.AreEqual(3, result.TotalCount);
        Assert.AreEqual(3, result.PageSize, "PageSize must report the effective count actually returned, not the literal 0 requested");
    }

    [TestMethod]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        var result = await _reader.GetByIdAsync(Guid.NewGuid());
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetByIdAsync_ExistingAction_ReturnsIt()
    {
        var entry = BuildPendingModify("BATCH-1");
        await _writer.WriteAsync(entry);

        var found = await _reader.GetByIdAsync(entry.Id);

        Assert.IsNotNull(found);
        Assert.AreEqual(entry.Id, found!.Id);
        Assert.AreEqual(ImportActionStatus.Pending, found.Status.Parsed);
        Assert.AreEqual(ImportActionKind.Modify, found.ActionType.Parsed);
    }

    /// <summary>
    /// Found live during #210's IdClauses refactor: Sql.SystemImportActions.SelectById was declared as
    /// a property, not a field, so it evaded both guard tests' reflection-based enumeration entirely
    /// and its "WHERE Id = @id" was never wrapped in UPPER() by the earlier #210 pass. Writing through
    /// <see cref="_writer"/> alone can't reproduce the gap — WriteAsync binds Id as a Guid-typed
    /// parameter, which GuidHandler force-uppercases at insert time too, so both sides would already
    /// agree by construction. This inserts a lowercase-stored row via raw SQL to prove the read side
    /// resolves it regardless, independent of how it was written.
    /// </summary>
    [TestMethod]
    public async Task GetByIdAsync_LowercaseStoredId_StillResolves()
    {
        var lowercaseId = Guid.NewGuid().ToString("D");
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            await conn.ExecuteAsync("""
                INSERT INTO System_ImportActions
                    (Id, BatchId, ActionType, EntityType, EntityId, IncomingValue, Status, DetectedAt, DateCreated, IsDeleted)
                VALUES
                    (@Id, 'BATCH-1', 'Add', 'Quote', @Id, '{}', 'Pending', @now, @now, 0);
                """, new { Id = lowercaseId, now = DateTime.UtcNow.ToString("O") });
        }

        var found = await _reader.GetByIdAsync(Guid.Parse(lowercaseId));

        Assert.IsNotNull(found, "GetByIdAsync must resolve regardless of the stored row's casing (#210) — the previously fully-unmitigated gap in Sql.SystemImportActions.SelectById.");
    }

    [TestMethod]
    public async Task WriteManyAsync_PersistsEveryAction()
    {
        var a1 = BuildDecidedAdd("BATCH-1");
        var a2 = BuildPendingModify("BATCH-1");

        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            await _writer.WriteManyAsync([a1, a2], conn);
        }

        var batch = await _reader.GetAllForBatchAsync("BATCH-1");
        Assert.AreEqual(2, batch.Count);
        CollectionAssert.AreEquivalent(new[] { a1.Id, a2.Id }, batch.Select(a => a.Id).ToList());
    }

    [TestMethod]
    public async Task GetAllForBatchAsync_ReturnsOnlyMatchingBatchRegardlessOfStatus()
    {
        var a1 = BuildPendingModify("BATCH-A");
        var a2 = BuildPendingModify("BATCH-A");
        var b1 = BuildPendingModify("BATCH-B");
        await _writer.WriteAsync(a1);
        await _writer.WriteAsync(a2);
        await _writer.WriteAsync(b1);

        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            await _writer.MarkAppliedAsync(a2.Id, conn);
        }

        var batchA = await _reader.GetAllForBatchAsync("BATCH-A");

        Assert.AreEqual(2, batchA.Count);
        CollectionAssert.AreEquivalent(new[] { a1.Id, a2.Id }, batchA.Select(a => a.Id).ToList());
    }

    [TestMethod]
    public async Task MarkDecidedAsync_TransitionsToDecidedAndStoresDecisionJson()
    {
        var entry = BuildPendingModify("BATCH-1");
        await _writer.WriteAsync(entry);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        await _writer.MarkDecidedAsync(entry.Id, """{"date":{"choice":"Replace"}}""", null, conn);

        var found = await _reader.GetByIdAsync(entry.Id);
        Assert.AreEqual(ImportActionStatus.Decided, found!.Status.Parsed);
        Assert.AreEqual("""{"date":{"choice":"Replace"}}""", found.MergedFields);
    }

    [TestMethod]
    public async Task MarkDecidedAsync_CalledAgain_OverwritesPriorDecision()
    {
        var entry = BuildPendingModify("BATCH-1");
        await _writer.WriteAsync(entry);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        await _writer.MarkDecidedAsync(entry.Id, "\"first\"", null, conn);
        await _writer.MarkDecidedAsync(entry.Id, "\"second\"", null, conn);

        var found = await _reader.GetByIdAsync(entry.Id);
        Assert.AreEqual(ImportActionStatus.Decided, found!.Status.Parsed);
        Assert.AreEqual("\"second\"", found.MergedFields);
    }

    [TestMethod]
    public async Task ClearDecisionAsync_RevertsToPendingAndClearsMergedFields()
    {
        var entry = BuildPendingModify("BATCH-1");
        await _writer.WriteAsync(entry);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        await _writer.MarkDecidedAsync(entry.Id, "\"decision\"", null, conn);
        await _writer.ClearDecisionAsync(entry.Id, conn);

        var found = await _reader.GetByIdAsync(entry.Id);
        Assert.AreEqual(ImportActionStatus.Pending, found!.Status.Parsed);
        Assert.IsNull(found.MergedFields);
    }

    [TestMethod]
    public async Task MarkAppliedAsync_SetsAppliedStatusAndAppliedAt()
    {
        var entry = BuildDecidedAdd("BATCH-1");
        await _writer.WriteAsync(entry);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        await _writer.MarkAppliedAsync(entry.Id, conn);

        var found = await _reader.GetByIdAsync(entry.Id);
        Assert.AreEqual(ImportActionStatus.Applied, found!.Status.Parsed);
        Assert.IsNotNull(found.AppliedAt);
    }

    [TestMethod]
    public async Task MarkBatchDiscardedAsync_DiscardsEveryActionInBatchOnly()
    {
        var a1 = BuildDecidedAdd("BATCH-A");
        var a2 = BuildPendingModify("BATCH-A");
        var b1 = BuildDecidedAdd("BATCH-B");
        await _writer.WriteAsync(a1);
        await _writer.WriteAsync(a2);
        await _writer.WriteAsync(b1);

        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            await _writer.MarkBatchDiscardedAsync("BATCH-A", conn);
        }

        var a1After = await _reader.GetByIdAsync(a1.Id);
        var a2After = await _reader.GetByIdAsync(a2.Id);
        var b1After = await _reader.GetByIdAsync(b1.Id);

        Assert.AreEqual(ImportActionStatus.Discarded, a1After!.Status.Parsed);
        Assert.IsNotNull(a1After.DiscardedAt);
        Assert.AreEqual(ImportActionStatus.Discarded, a2After!.Status.Parsed);
        Assert.AreEqual(ImportActionStatus.Decided, b1After!.Status.Parsed, "A different batch must be untouched.");
    }

    [TestMethod]
    public async Task ExistingBatchId_RoundTripsCorrectly()
    {
        var entry = BuildPendingModify("BATCH-2", existingBatchId: "BATCH-1");
        await _writer.WriteAsync(entry);

        var found = await _reader.GetByIdAsync(entry.Id);

        Assert.AreEqual("BATCH-1", found!.ExistingBatchId);
        Assert.AreEqual("BATCH-2", found.BatchId);
    }

    [TestMethod]
    public async Task AddAction_HasNoExistingValueOrMergedFields()
    {
        var entry = BuildDecidedAdd("BATCH-1");
        await _writer.WriteAsync(entry);

        var found = await _reader.GetByIdAsync(entry.Id);

        Assert.IsNull(found!.ExistingValue);
        Assert.IsNull(found.MergedFields);
        Assert.IsNotNull(found.IncomingValue);
    }
}

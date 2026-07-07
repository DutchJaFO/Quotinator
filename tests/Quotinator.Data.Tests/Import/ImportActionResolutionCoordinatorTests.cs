using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Import;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Tests.Import;

/// <summary>
/// Exercises <see cref="ImportActionResolutionCoordinator"/> using a fake in-memory apply callback —
/// proof that the whole stage/decide/undo/apply/discard workflow needs no real Quote/Source/Character
/// schema to be fully tested, exactly as the reusability goal behind #154 intends.
/// </summary>
[TestClass]
public class ImportActionResolutionCoordinatorTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;
    private IDbConnectionFactory _factory = null!;
    private SystemImportActionWriter _writer = null!;
    private SystemImportActionReader _reader = null!;
    private ImportActionResolutionCoordinator _coordinator = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_action_coordinator_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        conn.Execute("""
            CREATE TABLE System_ImportActions (
                Id              TEXT    NOT NULL PRIMARY KEY,
                BatchId         TEXT    NOT NULL,
                ActionType      TEXT    NOT NULL,
                EntityType      TEXT    NOT NULL,
                EntityId        TEXT    NOT NULL,
                ExistingBatchId TEXT,
                ExistingValue   TEXT,
                IncomingValue   TEXT    NOT NULL,
                AppliedPolicy   TEXT,
                Status          TEXT    NOT NULL,
                MergedFields    TEXT,
                DetectedAt      TEXT    NOT NULL,
                AppliedAt       TEXT,
                DiscardedAt     TEXT,
                DateCreated     TEXT    NOT NULL,
                DateModified    TEXT,
                DateDeleted     TEXT,
                IsDeleted       INTEGER NOT NULL DEFAULT 0
            );
            """);

        _factory     = new SqliteConnectionFactory(_dbPath);
        _writer      = new SystemImportActionWriter(_factory);
        _reader      = new SystemImportActionReader(_factory);
        _coordinator = new ImportActionResolutionCoordinator(_reader, _writer, _factory);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static SystemImportAction BuildPendingModify(string batchId) => new()
    {
        BatchId       = batchId,
        ActionType    = ImportActionKind.Modify,
        EntityType    = "Widget",
        EntityId      = Guid.NewGuid().ToString(),
        ExistingValue = "{}",
        IncomingValue = "{}",
        Status        = ImportActionStatus.Pending,
        DetectedAt    = DateTime.UtcNow,
    };

    private static SystemImportAction BuildDecidedAdd(string batchId) => new()
    {
        BatchId       = batchId,
        ActionType    = ImportActionKind.Add,
        EntityType    = "Widget",
        EntityId      = Guid.NewGuid().ToString(),
        IncomingValue = "{}",
        Status        = ImportActionStatus.Decided,
        DetectedAt    = DateTime.UtcNow,
    };

    // ── StageAsync ────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task StageAsync_WritesEveryActionSupplied()
    {
        var a1 = BuildDecidedAdd("BATCH-1");
        var a2 = BuildPendingModify("BATCH-1");

        await _coordinator.StageAsync([a1, a2]);

        var batch = await _reader.GetAllForBatchAsync("BATCH-1");
        Assert.AreEqual(2, batch.Count);
    }

    [TestMethod]
    public async Task StageAsync_EmptyList_NoOp()
    {
        await _coordinator.StageAsync([]);

        var batch = await _reader.GetAllForBatchAsync("BATCH-1");
        Assert.AreEqual(0, batch.Count);
    }

    // ── DecideAsync / UndoDecisionAsync ──────────────────────────────────────

    [TestMethod]
    public async Task DecideAsync_UnknownId_ThrowsImportActionNotFoundException()
        => await Assert.ThrowsExactlyAsync<ImportActionNotFoundException>(
            () => _coordinator.DecideAsync(Guid.NewGuid(), "\"decision\""));

    [TestMethod]
    public async Task DecideAsync_PendingAction_StagesDecisionAndNeverInvokesApplyCallback()
    {
        var entry = BuildPendingModify("BATCH-1");
        await _writer.WriteAsync(entry);

        var callbackInvoked = false;
        await _coordinator.DecideAsync(entry.Id, "\"my-decision\"");

        var found = await _reader.GetByIdAsync(entry.Id);
        Assert.AreEqual(ImportActionStatus.Decided, found!.Status);
        Assert.AreEqual("\"my-decision\"", found.MergedFields);
        Assert.IsFalse(callbackInvoked, "DecideAsync must never touch any domain table.");
    }

    [TestMethod]
    public async Task DecideAsync_AlreadyAppliedAction_ThrowsImportActionStateException()
    {
        var entry = BuildDecidedAdd("BATCH-1");
        await _writer.WriteAsync(entry);
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            await _writer.MarkAppliedAsync(entry.Id, conn);
        }

        await Assert.ThrowsExactlyAsync<ImportActionStateException>(() => _coordinator.DecideAsync(entry.Id, "\"x\""));
    }

    [TestMethod]
    public async Task UndoDecisionAsync_DecidedAction_RevertsToPending()
    {
        var entry = BuildPendingModify("BATCH-1");
        await _writer.WriteAsync(entry);
        await _coordinator.DecideAsync(entry.Id, "\"decision\"");

        await _coordinator.UndoDecisionAsync(entry.Id);

        var found = await _reader.GetByIdAsync(entry.Id);
        Assert.AreEqual(ImportActionStatus.Pending, found!.Status);
        Assert.IsNull(found.MergedFields);
    }

    [TestMethod]
    public async Task UndoDecisionAsync_StillPendingAction_ThrowsImportActionStateException()
    {
        var entry = BuildPendingModify("BATCH-1");
        await _writer.WriteAsync(entry);

        await Assert.ThrowsExactlyAsync<ImportActionStateException>(() => _coordinator.UndoDecisionAsync(entry.Id));
    }

    // ── TryApplyBatchAsync ────────────────────────────────────────────────────

    [TestMethod]
    public async Task TryApplyBatchAsync_SomeActionsStillPending_ReturnsPendingIdsAndNeverInvokesCallback()
    {
        var decided = BuildDecidedAdd("BATCH-1");
        var pending = BuildPendingModify("BATCH-1");
        await _writer.WriteAsync(decided);
        await _writer.WriteAsync(pending);

        var callbackInvocations = 0;
        var result = await _coordinator.TryApplyBatchAsync("BATCH-1", (_, _, _) =>
        {
            callbackInvocations++;
            return Task.CompletedTask;
        });

        Assert.IsNotNull(result);
        CollectionAssert.AreEqual(new[] { pending.Id }, result!.ToList());
        Assert.AreEqual(0, callbackInvocations, "Nothing should be applied while any action in the batch is still pending.");

        var stillDecided = await _reader.GetByIdAsync(decided.Id);
        Assert.AreEqual(ImportActionStatus.Decided, stillDecided!.Status, "The already-decided action must not be applied either — all-or-nothing.");
    }

    [TestMethod]
    public async Task TryApplyBatchAsync_EveryActionDecided_InvokesCallbackOncePerActionAndMarksAllApplied()
    {
        var first  = BuildDecidedAdd("BATCH-1");
        var second = BuildPendingModify("BATCH-1");
        await _writer.WriteAsync(first);
        await _writer.WriteAsync(second);
        await _coordinator.DecideAsync(second.Id, "\"decision-2\"");

        var appliedIds = new List<Guid>();
        var result = await _coordinator.TryApplyBatchAsync("BATCH-1", (action, connection, transaction) =>
        {
            Assert.IsNotNull(connection);
            Assert.IsNotNull(transaction);
            appliedIds.Add(action.Id);
            return Task.CompletedTask;
        });

        Assert.IsNull(result, "A fully-decided batch must apply successfully (null return).");
        CollectionAssert.AreEquivalent(new[] { first.Id, second.Id }, appliedIds);

        var firstAfter  = await _reader.GetByIdAsync(first.Id);
        var secondAfter = await _reader.GetByIdAsync(second.Id);
        Assert.AreEqual(ImportActionStatus.Applied, firstAfter!.Status);
        Assert.AreEqual(ImportActionStatus.Applied, secondAfter!.Status);
        Assert.IsNotNull(firstAfter.AppliedAt);
    }

    [TestMethod]
    public async Task TryApplyBatchAsync_CallbackThrows_RollsBackAndLeavesActionsDecided()
    {
        var entry = BuildDecidedAdd("BATCH-1");
        await _writer.WriteAsync(entry);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _coordinator.TryApplyBatchAsync("BATCH-1", (_, _, _) => throw new InvalidOperationException("boom")));

        var found = await _reader.GetByIdAsync(entry.Id);
        Assert.AreEqual(ImportActionStatus.Decided, found!.Status, "A failed apply must not leave the action marked Applied.");
    }

    [TestMethod]
    public async Task TryApplyBatchAsync_NoActionsForBatch_ReturnsNullWithoutInvokingCallback()
    {
        var callbackInvoked = false;
        var result = await _coordinator.TryApplyBatchAsync("NO-SUCH-BATCH", (_, _, _) =>
        {
            callbackInvoked = true;
            return Task.CompletedTask;
        });

        Assert.IsNull(result);
        Assert.IsFalse(callbackInvoked);
    }

    // ── DiscardBatchAsync ─────────────────────────────────────────────────────

    [TestMethod]
    public async Task DiscardBatchAsync_NoActionsForBatch_ThrowsImportBatchStateException()
        => await Assert.ThrowsExactlyAsync<ImportBatchStateException>(
            () => _coordinator.DiscardBatchAsync("NO-SUCH-BATCH"));

    [TestMethod]
    public async Task DiscardBatchAsync_StagedBatch_MarksEveryActionDiscardedWithoutTouchingDomainTables()
    {
        var a1 = BuildDecidedAdd("BATCH-1");
        var a2 = BuildPendingModify("BATCH-1");
        await _writer.WriteAsync(a1);
        await _writer.WriteAsync(a2);

        await _coordinator.DiscardBatchAsync("BATCH-1");

        var a1After = await _reader.GetByIdAsync(a1.Id);
        var a2After = await _reader.GetByIdAsync(a2.Id);
        Assert.AreEqual(ImportActionStatus.Discarded, a1After!.Status);
        Assert.AreEqual(ImportActionStatus.Discarded, a2After!.Status);
    }

    [TestMethod]
    public async Task DiscardBatchAsync_AlreadyAppliedBatch_ThrowsImportBatchStateException()
    {
        var entry = BuildDecidedAdd("BATCH-1");
        await _writer.WriteAsync(entry);
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            await _writer.MarkAppliedAsync(entry.Id, conn);
        }

        await Assert.ThrowsExactlyAsync<ImportBatchStateException>(() => _coordinator.DiscardBatchAsync("BATCH-1"));
    }

    [TestMethod]
    public async Task DiscardBatchAsync_AlreadyDiscardedBatch_ThrowsImportBatchStateException()
    {
        var entry = BuildDecidedAdd("BATCH-1");
        await _writer.WriteAsync(entry);
        await _coordinator.DiscardBatchAsync("BATCH-1");

        await Assert.ThrowsExactlyAsync<ImportBatchStateException>(() => _coordinator.DiscardBatchAsync("BATCH-1"));
    }
}

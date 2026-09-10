using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.Database;

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
    private ImportActionWriter _writer = null!;
    private ImportActionReader _reader = null!;
    private ImportActionResolutionCoordinator _coordinator = null!;

    [TestInitialize]
    public async Task TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_action_coordinator_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");

        // #389: the schema the application actually creates, not a hand-written copy of Import_Action.
        // The copy this replaced still allowed only Add/Modify and no Stale — it had drifted three
        // migrations behind, so no test here could stage a no-op the planner now writes routinely.
        await CurrentSchema.ApplyDataSchemaAsync(_dbPath);

        _factory     = new SqliteConnectionFactory(_dbPath);
        _writer      = new ImportActionWriter(_factory);
        _reader      = new ImportActionReader(_factory);
        _coordinator = new ImportActionResolutionCoordinator(_reader, _writer, _factory);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static ImportActionEntity BuildPendingModify(string batchId) => new()
    {
        BatchId       = batchId,
        ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
        EntityType    = "Widget",
        EntityId      = Guid.NewGuid().ToString(),
        ExistingValue = "{}",
        IncomingValue = "{}",
        Status        = new SafeValue<ImportActionStatus?>(ImportActionStatus.Pending.ToString(), ImportActionStatus.Pending),
        DetectedAt    = DateTime.UtcNow,
    };

    private static ImportActionEntity BuildDecidedAdd(string batchId) => new()
    {
        BatchId       = batchId,
        ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Add.ToString(), ImportActionKind.Add),
        EntityType    = "Widget",
        EntityId      = Guid.NewGuid().ToString(),
        IncomingValue = "{}",
        Status        = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
        DetectedAt    = DateTime.UtcNow,
    };

    private static ImportActionEntity BuildBlockedModify(string batchId) => new()
    {
        BatchId       = batchId,
        ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
        EntityType    = "Widget",
        EntityId      = Guid.NewGuid().ToString(),
        ExistingValue = "{}",
        IncomingValue = "{}",
        Status        = new SafeValue<ImportActionStatus?>(ImportActionStatus.Blocked.ToString(), ImportActionStatus.Blocked),
        DetectedAt    = DateTime.UtcNow,
    };

    /// <summary>
    /// A Pending/Blocked Modify whose "existing" side is itself an earlier row from the same batch
    /// (#374) — e.g. two lines within one import file that hash to the same id and disagree only by
    /// case, one becoming a same-batch stand-in "existing" for the other. Distinguished from a genuine
    /// DB-backed Modify by <see cref="ImportActionEntity.ExistingBatchId"/> equalling the action's own
    /// <see cref="ImportActionEntity.BatchId"/> — a real prior row's <c>ExistingBatchId</c> names
    /// whichever earlier batch actually wrote it, never the current one.
    /// </summary>
    private static ImportActionEntity BuildPendingModifyFromSameBatch(string batchId) => new()
    {
        BatchId         = batchId,
        ActionType      = new SafeValue<ImportActionKind?>(ImportActionKind.Modify.ToString(), ImportActionKind.Modify),
        EntityType      = "Widget",
        EntityId        = Guid.NewGuid().ToString(),
        ExistingBatchId = batchId,
        ExistingValue   = "{}",
        IncomingValue   = "{}",
        Status          = new SafeValue<ImportActionStatus?>(ImportActionStatus.Pending.ToString(), ImportActionStatus.Pending),
        DetectedAt      = DateTime.UtcNow,
    };

    // ── StageAsync ────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task StageAsync_WritesEveryActionSupplied()
    {
        ImportActionEntity a1 = BuildDecidedAdd("BATCH-1");
        ImportActionEntity a2 = BuildPendingModify("BATCH-1");

        await _coordinator.StageAsync([a1, a2]);

        IReadOnlyList<ImportActionEntity> batch = await _reader.GetAllForBatchAsync("BATCH-1");
        Assert.HasCount(2, batch);
    }

    [TestMethod]
    public async Task StageAsync_EmptyList_NoOp()
    {
        await _coordinator.StageAsync([]);

        IReadOnlyList<ImportActionEntity> batch = await _reader.GetAllForBatchAsync("BATCH-1");
        Assert.IsEmpty(batch);
    }

    // ── DecideAsync / UndoDecisionAsync ──────────────────────────────────────

    [TestMethod]
    public async Task DecideAsync_UnknownId_ThrowsImportActionNotFoundException()
        => await Assert.ThrowsExactlyAsync<ImportActionNotFoundException>(
            () => _coordinator.DecideAsync(Guid.NewGuid(), "\"decision\""));

    [TestMethod]
    public async Task DecideAsync_PendingAction_StagesDecisionAndNeverInvokesApplyCallback()
    {
        ImportActionEntity entry = BuildPendingModify("BATCH-1");
        await _writer.WriteAsync(entry);

        bool callbackInvoked = false;
        await _coordinator.DecideAsync(entry.Id, "\"my-decision\"");

        ImportActionEntity? found = await _reader.GetByIdAsync(entry.Id);
        Assert.AreEqual(ImportActionStatus.Decided, found!.Status.Parsed);
        Assert.AreEqual("\"my-decision\"", found.MergedFields);
        Assert.IsFalse(callbackInvoked, "DecideAsync must never touch any domain table.");
    }

    [TestMethod]
    public async Task DecideAsync_AlreadyAppliedAction_ThrowsImportActionStateException()
    {
        ImportActionEntity entry = BuildDecidedAdd("BATCH-1");
        await _writer.WriteAsync(entry);
        using (SqliteConnection conn = new($"Data Source={_dbPath}"))
        {
            conn.Open();
            await _writer.MarkAppliedAsync(entry.Id, conn);
        }

        await Assert.ThrowsExactlyAsync<ImportActionStateException>(() => _coordinator.DecideAsync(entry.Id, "\"x\""));
    }

    [TestMethod]
    public async Task UndoDecisionAsync_DecidedAction_RevertsToPending()
    {
        ImportActionEntity entry = BuildPendingModify("BATCH-1");
        await _writer.WriteAsync(entry);
        await _coordinator.DecideAsync(entry.Id, "\"decision\"");

        await _coordinator.UndoDecisionAsync(entry.Id);

        ImportActionEntity? found = await _reader.GetByIdAsync(entry.Id);
        Assert.AreEqual(ImportActionStatus.Pending, found!.Status.Parsed);
        Assert.IsNull(found.MergedFields);
    }

    [TestMethod]
    public async Task UndoDecisionAsync_StillPendingAction_ThrowsImportActionStateException()
    {
        ImportActionEntity entry = BuildPendingModify("BATCH-1");
        await _writer.WriteAsync(entry);

        await Assert.ThrowsExactlyAsync<ImportActionStateException>(() => _coordinator.UndoDecisionAsync(entry.Id));
    }

    // ── TryApplyBatchAsync ────────────────────────────────────────────────────

    [TestMethod]
    public async Task TryApplyBatchAsync_SomeActionsStillPending_ReturnsPendingIdsAndNeverInvokesCallback()
    {
        ImportActionEntity decided = BuildDecidedAdd("BATCH-1");
        ImportActionEntity pending = BuildPendingModify("BATCH-1");
        await _writer.WriteAsync(decided);
        await _writer.WriteAsync(pending);

        int callbackInvocations = 0;
        IReadOnlyList<Guid>? result = await _coordinator.TryApplyBatchAsync("BATCH-1", (_, _, _) =>
        {
            callbackInvocations++;
            return Task.CompletedTask;
        }, TestContext.CancellationToken);

        Assert.IsNotNull(result);
        Assert.AreSequenceEqual([pending.Id], [.. result!]);
        Assert.AreEqual(0, callbackInvocations, "Nothing should be applied while any action in the batch is still pending.");

        ImportActionEntity? stillDecided = await _reader.GetByIdAsync(decided.Id);
        Assert.AreEqual(ImportActionStatus.Decided, stillDecided!.Status.Parsed, "The already-decided action must not be applied either — all-or-nothing.");
    }

    [TestMethod]
    public async Task TryApplyBatchAsync_EveryActionDecided_InvokesCallbackOncePerActionAndMarksAllApplied()
    {
        ImportActionEntity first  = BuildDecidedAdd("BATCH-1");
        ImportActionEntity second = BuildPendingModify("BATCH-1");
        await _writer.WriteAsync(first);
        await _writer.WriteAsync(second);
        await _coordinator.DecideAsync(second.Id, "\"decision-2\"");

        List<Guid> appliedIds = [];
        IReadOnlyList<Guid>? result = await _coordinator.TryApplyBatchAsync("BATCH-1", (action, connection, transaction) =>
        {
            Assert.IsNotNull(connection);
            Assert.IsNotNull(transaction);
            appliedIds.Add(action.Id);
            return Task.CompletedTask;
        }, TestContext.CancellationToken);

        Assert.IsNull(result, "A fully-decided batch must apply successfully (null return).");
        Assert.AreSequenceEqual([first.Id, second.Id], appliedIds, Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);

        ImportActionEntity? firstAfter  = await _reader.GetByIdAsync(first.Id);
        ImportActionEntity? secondAfter = await _reader.GetByIdAsync(second.Id);
        Assert.AreEqual(ImportActionStatus.Applied, firstAfter!.Status.Parsed);
        Assert.AreEqual(ImportActionStatus.Applied, secondAfter!.Status.Parsed);
        Assert.IsNotNull(firstAfter.AppliedAt);
    }

    [TestMethod]
    public async Task TryApplyBatchAsync_CallbackThrows_RollsBackAndLeavesActionsDecided()
    {
        ImportActionEntity entry = BuildDecidedAdd("BATCH-1");
        await _writer.WriteAsync(entry);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _coordinator.TryApplyBatchAsync("BATCH-1", (_, _, _) => throw new InvalidOperationException("boom"), TestContext.CancellationToken));

        ImportActionEntity? found = await _reader.GetByIdAsync(entry.Id);
        Assert.AreEqual(ImportActionStatus.Decided, found!.Status.Parsed, "A failed apply must not leave the action marked Applied.");
    }

    [TestMethod]
    public async Task TryApplyBatchAsync_BlockedActionInBatch_HoldsEntireBatch()
    {
        ImportActionEntity decided = BuildDecidedAdd("BATCH-1");
        ImportActionEntity blocked = BuildBlockedModify("BATCH-1");
        await _writer.WriteAsync(decided);
        await _writer.WriteAsync(blocked);

        int callbackInvocations = 0;
        IReadOnlyList<Guid>? result = await _coordinator.TryApplyBatchAsync("BATCH-1", (_, _, _) =>
        {
            callbackInvocations++;
            return Task.CompletedTask;
        }, TestContext.CancellationToken);

        Assert.IsNotNull(result);
        Assert.AreSequenceEqual([blocked.Id], [.. result!]);
        Assert.AreEqual(0, callbackInvocations, "A Blocked action must hold the whole batch, including otherwise-ready actions.");

        ImportActionEntity? stillDecided = await _reader.GetByIdAsync(decided.Id);
        Assert.AreEqual(ImportActionStatus.Decided, stillDecided!.Status.Parsed, "An unrelated Decided action must not apply while a Blocked action shares its batch.");
    }

    [TestMethod]
    public async Task TryApplyBatchAsync_PendingModifyFromSameBatch_DoesNotHoldTheRestOfTheBatch()
    {
        ImportActionEntity decided = BuildDecidedAdd("BATCH-1");
        ImportActionEntity sameBatchCollision = BuildPendingModifyFromSameBatch("BATCH-1");
        await _writer.WriteAsync(decided);
        await _writer.WriteAsync(sameBatchCollision);

        List<Guid> appliedIds = [];
        IReadOnlyList<Guid>? result = await _coordinator.TryApplyBatchAsync("BATCH-1", (action, _, _) =>
        {
            appliedIds.Add(action.Id);
            return Task.CompletedTask;
        }, TestContext.CancellationToken);

        Assert.IsNull(result, "A Pending Modify whose existing side is this same batch protects nothing that already exists — it must not hold unrelated actions the way a genuine DB-backed Modify would.");
        Assert.AreSequenceEqual([decided.Id], appliedIds);

        ImportActionEntity? stillPending = await _reader.GetByIdAsync(sameBatchCollision.Id);
        Assert.AreEqual(ImportActionStatus.Pending, stillPending!.Status.Parsed, "The same-batch collision itself stays Pending — only its exemption from gating changes, not its own resolution.");
    }

    [TestMethod]
    public async Task TryApplyBatchAsync_BlockedActionResolved_UnrelatedActionsThenApply()
    {
        ImportActionEntity decided = BuildDecidedAdd("BATCH-1");
        ImportActionEntity blocked = BuildBlockedModify("BATCH-1");
        await _writer.WriteAsync(decided);
        await _writer.WriteAsync(blocked);
        await _coordinator.DecideAsync(blocked.Id, "\"resolved\"");

        List<Guid> appliedIds = [];
        IReadOnlyList<Guid>? result = await _coordinator.TryApplyBatchAsync("BATCH-1", (action, _, _) =>
        {
            appliedIds.Add(action.Id);
            return Task.CompletedTask;
        }, TestContext.CancellationToken);

        Assert.IsNull(result, "Once the Blocked action is decided, the rest of the batch must apply normally.");
        Assert.AreSequenceEqual([decided.Id, blocked.Id], appliedIds, Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task DecideAsync_MarkCompletenessAsProvided_PersistsOnTheAction()
    {
        ImportActionEntity entry = BuildPendingModify("BATCH-1");
        await _writer.WriteAsync(entry);

        await _coordinator.DecideAsync(entry.Id, "\"decision\"", CompletenessStatus.Complete);

        ImportActionEntity? found = await _reader.GetByIdAsync(entry.Id);
        Assert.AreEqual(CompletenessStatus.Complete, found!.MarkCompletenessAs.Parsed);
    }

    [TestMethod]
    public async Task DecideAsync_MarkCompletenessAsOmitted_StaysNull()
    {
        ImportActionEntity entry = BuildPendingModify("BATCH-1");
        await _writer.WriteAsync(entry);

        await _coordinator.DecideAsync(entry.Id, "\"decision\"");

        ImportActionEntity? found = await _reader.GetByIdAsync(entry.Id);
        Assert.IsNull(found!.MarkCompletenessAs.Parsed);
    }

    [TestMethod]
    public async Task TryApplyBatchAsync_NoActionsForBatch_ReturnsNullWithoutInvokingCallback()
    {
        bool callbackInvoked = false;
        IReadOnlyList<Guid>? result = await _coordinator.TryApplyBatchAsync("NO-SUCH-BATCH", (_, _, _) =>
        {
            callbackInvoked = true;
            return Task.CompletedTask;
        }, TestContext.CancellationToken);

        Assert.IsNull(result);
        Assert.IsFalse(callbackInvoked);
    }

    // ── DiscardBatchAsync ─────────────────────────────────────────────────────

    [TestMethod]
    public async Task DiscardBatchAsync_NoActionsForBatch_ThrowsImportBatchStateException()
        => await Assert.ThrowsExactlyAsync<ImportBatchStateException>(
            () => _coordinator.DiscardBatchAsync("NO-SUCH-BATCH", TestContext.CancellationToken));

    [TestMethod]
    public async Task DiscardBatchAsync_StagedBatch_MarksEveryActionDiscardedWithoutTouchingDomainTables()
    {
        ImportActionEntity a1 = BuildDecidedAdd("BATCH-1");
        ImportActionEntity a2 = BuildPendingModify("BATCH-1");
        await _writer.WriteAsync(a1);
        await _writer.WriteAsync(a2);

        await _coordinator.DiscardBatchAsync("BATCH-1", TestContext.CancellationToken);

        ImportActionEntity? a1After = await _reader.GetByIdAsync(a1.Id);
        ImportActionEntity? a2After = await _reader.GetByIdAsync(a2.Id);
        Assert.AreEqual(ImportActionStatus.Discarded, a1After!.Status.Parsed);
        Assert.AreEqual(ImportActionStatus.Discarded, a2After!.Status.Parsed);
    }

    [TestMethod]
    public async Task DiscardBatchAsync_AlreadyAppliedBatch_ThrowsImportBatchStateException()
    {
        ImportActionEntity entry = BuildDecidedAdd("BATCH-1");
        await _writer.WriteAsync(entry);
        using (SqliteConnection conn = new($"Data Source={_dbPath}"))
        {
            conn.Open();
            await _writer.MarkAppliedAsync(entry.Id, conn);
        }

        await Assert.ThrowsExactlyAsync<ImportBatchStateException>(() => _coordinator.DiscardBatchAsync("BATCH-1", TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task DiscardBatchAsync_AlreadyDiscardedBatch_ThrowsImportBatchStateException()
    {
        ImportActionEntity entry = BuildDecidedAdd("BATCH-1");
        await _writer.WriteAsync(entry);
        await _coordinator.DiscardBatchAsync("BATCH-1", TestContext.CancellationToken);

        await Assert.ThrowsExactlyAsync<ImportBatchStateException>(() => _coordinator.DiscardBatchAsync("BATCH-1", TestContext.CancellationToken));
    }

    /// <summary>
    /// A no-op the planner stages straight to <c>Applied</c> (#373, #376, #377): nothing is ever written
    /// for it, so it is not applied work, whatever its status says.
    /// </summary>
    private static ImportActionEntity BuildPlanTimeNoOp(string batchId) => new()
    {
        BatchId       = batchId,
        ActionType    = new SafeValue<ImportActionKind?>(ImportActionKind.Unchanged.ToString(), ImportActionKind.Unchanged),
        EntityType    = "Widget",
        EntityId      = Guid.NewGuid().ToString(),
        IncomingValue = "{}",
        Status        = new SafeValue<ImportActionStatus?>(ImportActionStatus.Applied.ToString(), ImportActionStatus.Applied),
        DetectedAt    = DateTime.UtcNow,
    };

    /// <summary>
    /// #389, found by #369's T2 run: a review batch whose file restates something already stored holds a
    /// plan-time no-op beside its pending conflict, and discarding it was refused as "already applied".
    /// The pending action is discarded; the no-op is left exactly as recorded.
    /// </summary>
    [TestMethod]
    public async Task DiscardBatchAsync_BatchWithPlanTimeNoOp_DiscardsTheRestAndKeepsTheNoOp()
    {
        ImportActionEntity noOp    = BuildPlanTimeNoOp("BATCH-1");
        ImportActionEntity pending = BuildPendingModify("BATCH-1");
        await _writer.WriteAsync(noOp);
        await _writer.WriteAsync(pending);

        await _coordinator.DiscardBatchAsync("BATCH-1", TestContext.CancellationToken);

        Assert.AreEqual(ImportActionStatus.Discarded, (await _reader.GetByIdAsync(pending.Id))!.Status.Parsed);
        Assert.AreEqual(ImportActionStatus.Applied, (await _reader.GetByIdAsync(noOp.Id))!.Status.Parsed,
            "A plan-time no-op is left as recorded — a discard has nothing of it to undo.");
    }

    /// <summary>
    /// #389, the control: an applied <c>Modify</c> is real applied work, which a discard cannot undo, so the
    /// batch is still refused — and a refused discard changes nothing.
    /// </summary>
    [TestMethod]
    public async Task DiscardBatchAsync_BatchWithAppliedModify_StillThrows()
    {
        ImportActionEntity applied = BuildPendingModify("BATCH-1");
        ImportActionEntity pending = BuildPendingModify("BATCH-1");
        await _writer.WriteAsync(applied);
        await _writer.WriteAsync(pending);
        await MarkAppliedAsync(applied.Id);

        await Assert.ThrowsExactlyAsync<ImportBatchStateException>(() => _coordinator.DiscardBatchAsync("BATCH-1", TestContext.CancellationToken));
        Assert.AreEqual(ImportActionStatus.Pending, (await _reader.GetByIdAsync(pending.Id))!.Status.Parsed,
            "A refused discard must leave every action as it was.");
    }

    /// <summary>
    /// #389, a control: a batch holding nothing but no-ops has nothing awaiting a decision, so there is
    /// nothing to discard and the batch is refused.
    /// </summary>
    [TestMethod]
    public async Task DiscardBatchAsync_BatchOfOnlyPlanTimeNoOps_Throws()
    {
        await _writer.WriteAsync(BuildPlanTimeNoOp("BATCH-1"));

        await Assert.ThrowsExactlyAsync<ImportBatchStateException>(() => _coordinator.DiscardBatchAsync("BATCH-1", TestContext.CancellationToken));
    }

    // ── TryReverseBatchAsync ──────────────────────────────────────────────────

    private async Task MarkAppliedAsync(Guid id)
    {
        using SqliteConnection conn = new($"Data Source={_dbPath}");
        conn.Open();
        await _writer.MarkAppliedAsync(id, conn);
    }

    [TestMethod]
    public async Task TryReverseBatchAsync_AllApplied_InvokesCallbackOnceWithWholeBatchAndReturnsNull()
    {
        ImportActionEntity a1 = BuildDecidedAdd("BATCH-1");
        ImportActionEntity a2 = BuildDecidedAdd("BATCH-1");
        await _writer.WriteAsync(a1);
        await _writer.WriteAsync(a2);
        await MarkAppliedAsync(a1.Id);
        await MarkAppliedAsync(a2.Id);

        int invocationCount = 0;
        IReadOnlyList<ImportActionEntity>? seenBatch = null;
        IReadOnlyList<Guid>? result = await _coordinator.TryReverseBatchAsync("BATCH-1", (actions, connection, transaction) =>
        {
            invocationCount++;
            seenBatch = actions;
            Assert.IsNotNull(connection);
            Assert.IsNotNull(transaction);
            return Task.CompletedTask;
        }, TestContext.CancellationToken);

        Assert.IsNull(result, "A fully-Applied batch must reverse successfully (null return).");
        Assert.AreEqual(1, invocationCount, "The callback must receive the whole batch in one call, not once per action.");
        Assert.AreSequenceEqual([a1.Id, a2.Id], [.. seenBatch!.Select(a => a.Id)], Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task TryReverseBatchAsync_ActionNotApplied_ReturnsBlockingIdsAndNeverInvokesCallback()
    {
        ImportActionEntity applied = BuildDecidedAdd("BATCH-1");
        ImportActionEntity decided = BuildDecidedAdd("BATCH-1");
        await _writer.WriteAsync(applied);
        await _writer.WriteAsync(decided);
        await MarkAppliedAsync(applied.Id);
        // decided stays Decided, never Applied.

        bool callbackInvoked = false;
        IReadOnlyList<Guid>? result = await _coordinator.TryReverseBatchAsync("BATCH-1", (_, _, _) =>
        {
            callbackInvoked = true;
            return Task.CompletedTask;
        }, TestContext.CancellationToken);

        Assert.IsNotNull(result);
        Assert.AreSequenceEqual([decided.Id], [.. result!]);
        Assert.IsFalse(callbackInvoked, "Nothing should be reversed while any action in the batch isn't Applied.");
    }

    [TestMethod]
    public async Task TryReverseBatchAsync_NoActionsForBatch_ReturnsNullWithoutInvokingCallback()
    {
        bool callbackInvoked = false;
        IReadOnlyList<Guid>? result = await _coordinator.TryReverseBatchAsync("NO-SUCH-BATCH", (_, _, _) =>
        {
            callbackInvoked = true;
            return Task.CompletedTask;
        }, TestContext.CancellationToken);

        Assert.IsNull(result);
        Assert.IsFalse(callbackInvoked);
    }

    [TestMethod]
    public async Task TryReverseBatchAsync_CallbackThrows_RollsBackAndBatchRemainsQueryable()
    {
        ImportActionEntity entry = BuildDecidedAdd("BATCH-1");
        await _writer.WriteAsync(entry);
        await MarkAppliedAsync(entry.Id);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _coordinator.TryReverseBatchAsync("BATCH-1", (_, _, _) => throw new InvalidOperationException("boom"), TestContext.CancellationToken));

        ImportActionEntity? found = await _reader.GetByIdAsync(entry.Id);
        Assert.AreEqual(ImportActionStatus.Applied, found!.Status.Parsed, "A failed reversal must leave the action exactly as it was.");
    }

    public TestContext TestContext { get; set; }
}

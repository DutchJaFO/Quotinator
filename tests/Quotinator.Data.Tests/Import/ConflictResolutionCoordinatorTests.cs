using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Tests.Import;

/// <summary>
/// Exercises <see cref="ConflictResolutionCoordinator"/> using a fake in-memory apply callback — proof
/// that the whole staging/undo/readiness-checking workflow needs no real Quote/Source/Character schema
/// to be fully tested, exactly as the reusability goal behind #149 intends.
/// </summary>
[TestClass]
public class ConflictResolutionCoordinatorTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;
    private IDbConnectionFactory _factory = null!;
    private SystemImportConflictWriter _writer = null!;
    private SystemImportConflictReader _reader = null!;
    private ConflictResolutionCoordinator _coordinator = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_coordinator_test_").FullName;
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
                Status          TEXT    NOT NULL CHECK (Status IN ('Pending', 'Decided', 'Resolved')),
                MergedFields    TEXT,
                DetectedAt      TEXT    NOT NULL,
                ResolvedAt      TEXT,
                DateCreated     TEXT    NOT NULL,
                DateModified    TEXT,
                DateDeleted     TEXT,
                IsDeleted       INTEGER NOT NULL DEFAULT 0
            );
            """);

        _factory     = new SqliteConnectionFactory(_dbPath);
        _writer      = new SystemImportConflictWriter(_factory);
        _reader      = new SystemImportConflictReader(_factory);
        _coordinator = new ConflictResolutionCoordinator(_reader, _writer, _factory);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static SystemImportConflict BuildPendingConflict(string batchId) => new()
    {
        BatchId       = batchId,
        EntityType    = "Widget",
        EntityId      = Guid.NewGuid().ToString(),
        ExistingValue = "{}",
        IncomingValue = "{}",
        Status        = new SafeValue<ImportConflictStatus?>(ImportConflictStatus.Pending.ToString(), ImportConflictStatus.Pending),
        DetectedAt    = DateTime.UtcNow,
    };

    // ── DecideAsync / UndoDecisionAsync ──────────────────────────────────────

    [TestMethod]
    public async Task DecideAsync_UnknownId_ThrowsConflictNotFoundException()
        => await Assert.ThrowsExactlyAsync<ConflictNotFoundException>(
            () => _coordinator.DecideAsync(Guid.NewGuid(), "\"decision\""));

    [TestMethod]
    public async Task DecideAsync_PendingConflict_StagesDecisionAndNeverInvokesApplyCallback()
    {
        var entry = BuildPendingConflict("BATCH-1");
        await _writer.WriteAsync(entry);

        var callbackInvoked = false;
        await _coordinator.DecideAsync(entry.Id, "\"my-decision\"");

        var found = await _reader.GetByIdAsync(entry.Id);
        Assert.AreEqual(ImportConflictStatus.Decided, found!.Status.Parsed);
        Assert.AreEqual("\"my-decision\"", found.MergedFields);
        Assert.IsFalse(callbackInvoked, "DecideAsync must never touch any domain table.");
    }

    [TestMethod]
    public async Task DecideAsync_AlreadyResolvedConflict_ThrowsConflictStateException()
    {
        var entry = BuildPendingConflict("BATCH-1");
        await _writer.WriteAsync(entry);
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            await _writer.MarkResolvedAsync(entry.Id, conn);
        }

        await Assert.ThrowsExactlyAsync<ConflictStateException>(() => _coordinator.DecideAsync(entry.Id, "\"x\""));
    }

    [TestMethod]
    public async Task UndoDecisionAsync_DecidedConflict_RevertsToPending()
    {
        var entry = BuildPendingConflict("BATCH-1");
        await _writer.WriteAsync(entry);
        await _coordinator.DecideAsync(entry.Id, "\"decision\"");

        await _coordinator.UndoDecisionAsync(entry.Id);

        var found = await _reader.GetByIdAsync(entry.Id);
        Assert.AreEqual(ImportConflictStatus.Pending, found!.Status.Parsed);
        Assert.IsNull(found.MergedFields);
    }

    [TestMethod]
    public async Task UndoDecisionAsync_StillPendingConflict_ThrowsConflictStateException()
    {
        var entry = BuildPendingConflict("BATCH-1");
        await _writer.WriteAsync(entry);

        await Assert.ThrowsExactlyAsync<ConflictStateException>(() => _coordinator.UndoDecisionAsync(entry.Id));
    }

    // ── TryApplyBatchAsync ────────────────────────────────────────────────────

    [TestMethod]
    public async Task TryApplyBatchAsync_SomeConflictsStillPending_ReturnsPendingIdsAndNeverInvokesCallback()
    {
        var decided = BuildPendingConflict("BATCH-1");
        var pending = BuildPendingConflict("BATCH-1");
        await _writer.WriteAsync(decided);
        await _writer.WriteAsync(pending);
        await _coordinator.DecideAsync(decided.Id, "\"decision\"");

        var callbackInvocations = 0;
        var result = await _coordinator.TryApplyBatchAsync("BATCH-1", (_, _, _) =>
        {
            callbackInvocations++;
            return Task.CompletedTask;
        });

        Assert.IsNotNull(result);
        CollectionAssert.AreEqual(new[] { pending.Id }, result!.ToList());
        Assert.AreEqual(0, callbackInvocations, "Nothing should be applied while any conflict in the batch is still pending.");

        var stillDecided = await _reader.GetByIdAsync(decided.Id);
        Assert.AreEqual(ImportConflictStatus.Decided, stillDecided!.Status.Parsed, "The already-decided conflict must not be resolved either — all-or-nothing.");
    }

    [TestMethod]
    public async Task TryApplyBatchAsync_EveryConflictDecided_InvokesCallbackOncePerConflictAndMarksAllResolved()
    {
        var first  = BuildPendingConflict("BATCH-1");
        var second = BuildPendingConflict("BATCH-1");
        await _writer.WriteAsync(first);
        await _writer.WriteAsync(second);
        await _coordinator.DecideAsync(first.Id, "\"decision-1\"");
        await _coordinator.DecideAsync(second.Id, "\"decision-2\"");

        var appliedIds = new List<Guid>();
        var result = await _coordinator.TryApplyBatchAsync("BATCH-1", (conflict, connection, transaction) =>
        {
            Assert.IsNotNull(connection);
            Assert.IsNotNull(transaction);
            appliedIds.Add(conflict.Id);
            return Task.CompletedTask;
        });

        Assert.IsNull(result, "A fully-decided batch must apply successfully (null return).");
        CollectionAssert.AreEquivalent(new[] { first.Id, second.Id }, appliedIds);

        var firstAfter  = await _reader.GetByIdAsync(first.Id);
        var secondAfter = await _reader.GetByIdAsync(second.Id);
        Assert.AreEqual(ImportConflictStatus.Resolved, firstAfter!.Status.Parsed);
        Assert.AreEqual(ImportConflictStatus.Resolved, secondAfter!.Status.Parsed);
        Assert.IsNotNull(firstAfter.ResolvedAt);
    }

    [TestMethod]
    public async Task TryApplyBatchAsync_CallbackThrows_RollsBackAndLeavesConflictsDecided()
    {
        var entry = BuildPendingConflict("BATCH-1");
        await _writer.WriteAsync(entry);
        await _coordinator.DecideAsync(entry.Id, "\"decision\"");

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _coordinator.TryApplyBatchAsync("BATCH-1", (_, _, _) => throw new InvalidOperationException("boom")));

        var found = await _reader.GetByIdAsync(entry.Id);
        Assert.AreEqual(ImportConflictStatus.Decided, found!.Status.Parsed, "A failed apply must not leave the conflict marked Resolved.");
    }

    [TestMethod]
    public async Task TryApplyBatchAsync_NoConflictsForBatch_ReturnsNullWithoutInvokingCallback()
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
}

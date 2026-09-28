using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Api.Enums;
using Quotinator.Api.Services;
using Quotinator.Api.Startup;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Enums;
using Quotinator.Data.Testing.Fakes;
using Quotinator.Data.Entities;
using Quotinator.Data.Import;
using Quotinator.Data.Notifications;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Api.Tests.Services;

/// <summary>Exercises <see cref="NotificationActionExecutor"/> (#278, #81).</summary>
[TestClass]
public class NotificationActionExecutorTests
{
    private sealed class FakeVersionService : IVersionService
    {
        public string Version => "1.8.3";
        public string Application => "Quotinator.Api";
    }

    private sealed class SpyAppVersionTracker : IAppVersionTracker
    {
        /// <summary>Every recorded pair, in call order, kept as the pair, since #312 made the pair the identity.</summary>
        public List<(string Application, string Version)> Recorded { get; } = [];

        public Task<AppVersionRecord?> GetLastActiveAsync() => Task.FromResult<AppVersionRecord?>(null);

        public Task<AppVersionRecord> RecordCurrentAsync(string application, string version)
        {
            Recorded.Add((application, version));
            return Task.FromResult(new AppVersionRecord(Guid.NewGuid(), application, version));
        }
    }

    private static ImportReviewPendingMetadataDto ReviewPayload(string batchId) => new()
    {
        FileName     = "curated.json",
        Origin       = FileResourceOrigin.System,
        BatchId      = batchId,
        Counts       = [new ImportReviewCountDto { Status = nameof(ImportActionStatus.Pending), Count = 2 }],
        ReleaseState = NotificationReleaseState.NotApplicable,
    };

    /// <summary>
    /// #303: the alert carries the coarse, whole-batch form of the two options the review page offers
    /// per action, so the common case does not require navigating first.
    /// </summary>
    [TestMethod]
    public async Task ImportReviewResolved_KeepExisting_DecidesEveryActionInTheBatch()
    {
        string batchId = Guid.NewGuid().ToString("D");
        FakeImportActionService importActions = new();
        NotificationActionExecutor executor = new(
            new SpyDatabaseInitializer(), new DatabaseHealthState(), new FakeNotificationWriter(),
            new SpyAppVersionTracker(), new FakeVersionService(), NullLogger<NotificationActionExecutor>.Instance, importActions, new FakeImportBatchRepository(), NoBackups(), BackupsFor(new SpyDatabaseInitializer()), NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance, new NotificationConditionChecks([], NullLogger<NotificationConditionChecks>.Instance));

        await executor.ExecuteAsync(
            NotificationDismissTrigger.ImportReviewResolved, ReviewPayload(batchId), FieldResolutionChoice.Keep);

        Assert.Contains((batchId, FieldResolutionChoice.Keep), importActions.DecideBatchCalls,
            "The alert's own payload names the batch, so the action resolves that batch and no other.");
    }

    /// <summary>
    /// The alert's remedy applies the batch it decided. Found in T2 (2026-09-01): the executor decided
    /// and stopped, leaving every action <c>Decided</c> and never <c>Applied</c>, so the operator's
    /// choice never reached the data, and the alert stayed active telling them to make it again
    /// (dismissal is wired to <c>ApplyBatchAsync</c>/<c>DiscardBatchAsync</c>, not to deciding).
    /// </summary>
    [TestMethod]
    public async Task ImportReviewResolved_AppliesTheBatchSoTheChoiceReachesTheData()
    {
        string batchId = Guid.NewGuid().ToString("D");
        FakeImportActionService importActions = new();
        NotificationActionExecutor executor = new(
            new SpyDatabaseInitializer(), new DatabaseHealthState(), new FakeNotificationWriter(),
            new SpyAppVersionTracker(), new FakeVersionService(), NullLogger<NotificationActionExecutor>.Instance, importActions, new FakeImportBatchRepository(), NoBackups(), BackupsFor(new SpyDatabaseInitializer()), NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance, new NotificationConditionChecks([], NullLogger<NotificationConditionChecks>.Instance));

        await executor.ExecuteAsync(
            NotificationDismissTrigger.ImportReviewResolved, ReviewPayload(batchId), FieldResolutionChoice.Replace);

        Assert.AreEqual(batchId, importActions.LastAppliedBatchId,
            "A decision that is never applied changes nothing and leaves the alert active.");
    }

    /// <summary>
    /// Nothing is applied when no choice was given: the throw must happen before any write, or a
    /// rejected request would still have moved the batch on.
    /// </summary>
    [TestMethod]
    public async Task ImportReviewResolved_WithoutAChoice_AppliesNothing()
    {
        FakeImportActionService importActions = new();
        NotificationActionExecutor executor = new(
            new SpyDatabaseInitializer(), new DatabaseHealthState(), new FakeNotificationWriter(),
            new SpyAppVersionTracker(), new FakeVersionService(), NullLogger<NotificationActionExecutor>.Instance, importActions, new FakeImportBatchRepository(), NoBackups(), BackupsFor(new SpyDatabaseInitializer()), NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance, new NotificationConditionChecks([], NullLogger<NotificationConditionChecks>.Instance));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(NotificationDismissTrigger.ImportReviewResolved, ReviewPayload(Guid.NewGuid().ToString("D"))));

        Assert.IsNull(importActions.LastAppliedBatchId);
    }

    /// <summary>
    /// No default side. Choosing one on the operator's behalf would silently overwrite their data with
    /// whichever way the code happened to lean; keeping and replacing are not interchangeable.
    /// </summary>
    [TestMethod]
    public async Task ImportReviewResolved_WithoutAChoice_Throws()
    {
        FakeImportActionService importActions = new();
        NotificationActionExecutor executor = new(
            new SpyDatabaseInitializer(), new DatabaseHealthState(), new FakeNotificationWriter(),
            new SpyAppVersionTracker(), new FakeVersionService(), NullLogger<NotificationActionExecutor>.Instance, importActions, new FakeImportBatchRepository(), NoBackups(), BackupsFor(new SpyDatabaseInitializer()), NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance, new NotificationConditionChecks([], NullLogger<NotificationConditionChecks>.Instance));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(NotificationDismissTrigger.ImportReviewResolved, ReviewPayload(Guid.NewGuid().ToString("D"))));

        Assert.IsEmpty(importActions.DecideBatchCalls, "Nothing may be decided when no side was chosen.");
    }

    /// <summary>Without the alert's payload there is no batch to act on, and acting on all of them would be worse than refusing.</summary>
    [TestMethod]
    public async Task ImportReviewResolved_WithoutItsPayload_Throws()
    {
        FakeImportActionService importActions = new();
        NotificationActionExecutor executor = new(
            new SpyDatabaseInitializer(), new DatabaseHealthState(), new FakeNotificationWriter(),
            new SpyAppVersionTracker(), new FakeVersionService(), NullLogger<NotificationActionExecutor>.Instance, importActions, new FakeImportBatchRepository(), NoBackups(), BackupsFor(new SpyDatabaseInitializer()), NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance, new NotificationConditionChecks([], NullLogger<NotificationConditionChecks>.Instance));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(NotificationDismissTrigger.ImportReviewResolved, metadata: null, FieldResolutionChoice.Keep));

        Assert.IsEmpty(importActions.DecideBatchCalls);
    }

    /// <summary>The trigger is executable, so `NotificationTable` renders its controls.</summary>
    [TestMethod]
    public void CanExecute_ImportReviewResolved_ReturnsTrue()
    {
        NotificationActionExecutor executor = new(
            new SpyDatabaseInitializer(), new DatabaseHealthState(), new FakeNotificationWriter(),
            new SpyAppVersionTracker(), new FakeVersionService(), NullLogger<NotificationActionExecutor>.Instance, new FakeImportActionService(), new FakeImportBatchRepository(), NoBackups(), BackupsFor(new SpyDatabaseInitializer()), NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance, new NotificationConditionChecks([], NullLogger<NotificationConditionChecks>.Instance));

        Assert.IsTrue(executor.CanExecute(NotificationDismissTrigger.ImportReviewResolved));
    }

    [TestMethod]
    public void CanExecute_DatabaseReset_ReturnsTrue()
    {
        NotificationActionExecutor executor = new(
            new SpyDatabaseInitializer(), new DatabaseHealthState(), new FakeNotificationWriter(),
            new SpyAppVersionTracker(), new FakeVersionService(), NullLogger<NotificationActionExecutor>.Instance, new FakeImportActionService(), new FakeImportBatchRepository(), NoBackups(), BackupsFor(new SpyDatabaseInitializer()), NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance, new NotificationConditionChecks([], NullLogger<NotificationConditionChecks>.Instance));

        Assert.IsTrue(executor.CanExecute(NotificationDismissTrigger.DatabaseReset));
    }

    /// <summary>#304: the Reseed trigger is executable, so `NotificationTable` renders its Run → Confirm control.</summary>
    [TestMethod]
    public void CanExecute_Reseed_ReturnsTrue()
    {
        NotificationActionExecutor executor = new(
            new SpyDatabaseInitializer(), new DatabaseHealthState(), new FakeNotificationWriter(),
            new SpyAppVersionTracker(), new FakeVersionService(), NullLogger<NotificationActionExecutor>.Instance, new FakeImportActionService(), new FakeImportBatchRepository(), NoBackups(), BackupsFor(new SpyDatabaseInitializer()), NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance, new NotificationConditionChecks([], NullLogger<NotificationConditionChecks>.Instance));

        Assert.IsTrue(executor.CanExecute(NotificationDismissTrigger.Reseed));
    }

    /// <summary>#304: running the action reseeds, then clears the recommendation it resolved.</summary>
    [TestMethod]
    public async Task ExecuteAsync_Reseed_CallsReseedAndDismissesMatchingNotifications()
    {
        SpyDatabaseInitializer dbInitializer = new();
        FakeNotificationWriter notificationWriter = new();
        NotificationActionExecutor executor = new(
            dbInitializer, new DatabaseHealthState(), notificationWriter,
            new SpyAppVersionTracker(), new FakeVersionService(), NullLogger<NotificationActionExecutor>.Instance, new FakeImportActionService(), new FakeImportBatchRepository(), NoBackups(), BackupsFor(new SpyDatabaseInitializer()), NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance, new NotificationConditionChecks([], NullLogger<NotificationConditionChecks>.Instance));

        await executor.ExecuteAsync(NotificationDismissTrigger.Reseed);

        Assert.IsTrue(dbInitializer.ReseedCalled);
        Assert.IsFalse(dbInitializer.ReseedForcedSourceRefresh,
            "The content is already downloaded by the time the recommendation exists; forcing another "
            + "network round-trip would be redundant.");
        Assert.AreSequenceEqual([NotificationDismissTrigger.Reseed], notificationWriter.DismissByTriggerCalls);

        // #308, the third defect T2 found: the dismissal happened and carried no resolution, so the row
        // read Done while saying nothing about what settled it. Asserting the trigger alone passed
        // throughout: the fake was discarding the resolution argument entirely.
        Assert.AreSequenceEqual([NotificationResolution.Reseeded], notificationWriter.DismissByTriggerResolutions,
            "A reseed run from its own notification must record that a reseed is what resolved it.");
    }

    /// <summary>
    /// #304: the Reseed case deliberately does *not* copy two steps from the DatabaseReset case beside
    /// it. A reseed replaces content within an intact schema (it neither degrades health nor empties
    /// System_AppVersion), so marking healthy or re-recording the version would assert a recovery that
    /// never happened. The likeliest defect here is copy-paste, which is exactly what this catches.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_Reseed_DoesNotTouchDatabaseHealthOrAppVersion()
    {
        DatabaseHealthState health = new();
        health.MarkFailed("some prior failure");
        SpyAppVersionTracker appVersionTracker = new();
        NotificationActionExecutor executor = new(
            new SpyDatabaseInitializer(), health, new FakeNotificationWriter(),
            appVersionTracker, new FakeVersionService(), NullLogger<NotificationActionExecutor>.Instance, new FakeImportActionService(), new FakeImportBatchRepository(), NoBackups(), BackupsFor(new SpyDatabaseInitializer()), NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance, new NotificationConditionChecks([], NullLogger<NotificationConditionChecks>.Instance));

        await executor.ExecuteAsync(NotificationDismissTrigger.Reseed);

        Assert.IsFalse(health.IsHealthy,
            "A reseed says nothing about whether a prior failure was resolved; only Reset rebuilds the schema.");
        Assert.IsEmpty(appVersionTracker.Recorded,
            "A reseed does not wipe System_AppVersion, so there is no history to re-populate.");
    }

    [TestMethod]
    public async Task ExecuteAsync_DatabaseReset_CallsResetAndMarksHealthyAndDismissesMatchingNotifications()
    {
        SpyDatabaseInitializer dbInitializer = new();
        DatabaseHealthState health = new();
        health.MarkFailed("some prior failure");
        FakeNotificationWriter notificationWriter = new();
        SpyAppVersionTracker appVersionTracker = new();
        NotificationActionExecutor executor = new(
            dbInitializer, health, notificationWriter, appVersionTracker, new FakeVersionService(), NullLogger<NotificationActionExecutor>.Instance, new FakeImportActionService(), new FakeImportBatchRepository(), NoBackups(), BackupsFor(new SpyDatabaseInitializer()), NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance, new NotificationConditionChecks([], NullLogger<NotificationConditionChecks>.Instance));

        await executor.ExecuteAsync(NotificationDismissTrigger.DatabaseReset);

        Assert.IsTrue(dbInitializer.ResetCalled);
        Assert.IsTrue(health.IsHealthy);
        Assert.HasCount(1, notificationWriter.DismissByTriggerCalls);
        Assert.AreEqual(NotificationDismissTrigger.DatabaseReset, notificationWriter.DismissByTriggerCalls[0]);
        Assert.AreSequenceEqual([NotificationResolution.Reset], notificationWriter.DismissByTriggerResolutions,
            "A reset run from its own notification must record that a reset is what resolved it (#308).");
        Assert.AreSequenceEqual([("Quotinator.Api", "1.8.3")], appVersionTracker.Recorded,
            "Reset must re-populate System_AppVersion immediately, matching AdminEndpoints.cs's own wiring.");
    }

    /// <summary>
    /// The originating notification's payload reaches the executor (#312 step 7). DatabaseReset ignores
    /// it (a schema-version overshoot is resolved for the whole database, so there is nothing to narrow),
    /// but the channel has to be proven to carry the value, or #304's Reseed inherits an untested seam
    /// rather than a working one.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_WithMetadata_DeliversItAndStillPerformsTheAction()
    {
        SpyDatabaseInitializer dbInitializer = new();
        RecordingExecutor executor = new();

        SchemaVersionOvershootMetadataDto metadata = new()
        {
            DataSchemaVersion = 7,
            AppSchemaVersion  = 5,
            ReleaseState      = NotificationReleaseState.NotApplicable,
        };
        await executor.ExecuteAsync(NotificationDismissTrigger.DatabaseReset, metadata);

        Assert.AreSame(metadata, executor.ReceivedMetadata,
            "The payload must arrive at the executor unchanged: not re-serialized, and not dropped.");
        Assert.IsFalse(dbInitializer.ResetCalled, "Sanity check: this test drives the recording double, not the real executor.");
    }

    /// <summary>A notification with no payload still executes; every row written before #312 is this case.</summary>
    [TestMethod]
    public async Task ExecuteAsync_WithoutMetadata_StillPerformsTheAction()
    {
        SpyDatabaseInitializer dbInitializer = new();
        DatabaseHealthState health = new();
        NotificationActionExecutor executor = new(
            dbInitializer, health, new FakeNotificationWriter(), new SpyAppVersionTracker(),
            new FakeVersionService(), NullLogger<NotificationActionExecutor>.Instance, new FakeImportActionService(), new FakeImportBatchRepository(), NoBackups(), BackupsFor(new SpyDatabaseInitializer()), NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance, new NotificationConditionChecks([], NullLogger<NotificationConditionChecks>.Instance));

        await executor.ExecuteAsync(NotificationDismissTrigger.DatabaseReset);

        Assert.IsTrue(dbInitializer.ResetCalled);
    }

    // ── #369: an action whose volatile subject is gone ─────────────────────────────────────────────

    // Hex letters in both, so a case-insensitive match is actually exercised.
    private const string LiveBatch = "7f00000a-0000-4000-8000-00000000000b";
    private const string GoneBatch = "7f00000c-0000-4000-8000-00000000000d";

    private static NotificationActionExecutor CreateExecutor(
        FakeImportBatchRepository? importBatches = null, SpyDatabaseInitializer? dbInitializer = null, IDatabaseBackupReader? backupReader = null,
        DatabaseHealthState? health = null, FakeNotificationWriter? notificationWriter = null, RecordingAuditEntryWriter? auditWriter = null,
        ILogger<NotificationActionExecutor>? logger = null, string? backupsFolder = null, IDatabaseBackupWriter? backupWriter = null,
        NotificationConditionChecks? conditionChecks = null)
    {
        SpyDatabaseInitializer db = dbInitializer ?? new SpyDatabaseInitializer();
        IAuditEntryWriter audit = (IAuditEntryWriter?)auditWriter ?? NoOpAuditEntryWriter.Instance;
        return new(
            db, health ?? new DatabaseHealthState(), notificationWriter ?? new FakeNotificationWriter(),
            new SpyAppVersionTracker(), new FakeVersionService(), logger ?? NullLogger<NotificationActionExecutor>.Instance,
            new FakeImportActionService(), importBatches ?? new FakeImportBatchRepository(),
            backupReader ?? (backupsFolder is null ? NoBackups() : BackupsIn(backupsFolder)),
            BackupsFor(db, audit, backupsFolder, backupWriter), audit, NoOpCallerContext.Instance,
            conditionChecks ?? new NotificationConditionChecks([], NullLogger<NotificationConditionChecks>.Instance));
    }

    /// <summary>The audited take-and-remove, over the given initializer and, where given, a real backups folder.</summary>
    private static BackupOperations BackupsFor(
        SpyDatabaseInitializer db, IAuditEntryWriter? auditWriter = null, string? backupsFolder = null, IDatabaseBackupWriter? backupWriter = null) => new(
        db,
        backupWriter ?? new DatabaseBackupWriter(new DatabaseOptions
        {
            DbPath      = "unused.db",
            BackupsPath = backupsFolder ?? Path.Combine(Path.GetTempPath(), "quotinator-348-none-" + Guid.NewGuid().ToString("N")),
        }),
        auditWriter ?? NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance, NullLogger<BackupOperations>.Instance);

    /// <summary>
    /// #369: an import-review alert outlives its batch, and once the batch is gone Keep and Take have
    /// nothing to be applied against. Answered from the alert's own payload and the availability read
    /// once per render, never by a query of the executor's own.
    /// </summary>
    [TestMethod]
    public void CanExecute_ImportReviewWhoseBatchIsGone_IsFalse()
        => Assert.IsFalse(CreateExecutor().CanExecute(
            NotificationDismissTrigger.ImportReviewResolved, ReviewPayload(GoneBatch), new NotificationActionAvailability([LiveBatch])));

    /// <summary>
    /// #369, the control: the same alert naming a batch that still exists stays executable, matched
    /// case-insensitively, since the payload and the batch row hold independently-cased copies of one id.
    /// </summary>
    [TestMethod]
    public void CanExecute_ImportReviewWithLiveBatch_IsTrue()
        => Assert.IsTrue(CreateExecutor().CanExecute(
            NotificationDismissTrigger.ImportReviewResolved, ReviewPayload(LiveBatch), new NotificationActionAvailability([LiveBatch.ToUpperInvariant()])));

    /// <summary>
    /// #369: without its payload an import-review action cannot run at all (<c>ExecuteAsync</c> throws
    /// for exactly this case), so the capability check has to say so before the action is offered.
    /// </summary>
    [TestMethod]
    public void CanExecute_ImportReviewWithoutItsPayload_IsFalse()
        => Assert.IsFalse(CreateExecutor().CanExecute(
            NotificationDismissTrigger.ImportReviewResolved, metadata: null, new NotificationActionAvailability([LiveBatch])));


    /// <summary>
    /// #369: the availability reports every batch that still exists, and only those, or a batch that is
    /// gone would read as present and its impossible actions would stay on offer.
    /// </summary>
    [TestMethod]
    public async Task GetAvailabilityAsync_ReportsEveryLiveBatchAndNoOther()
    {
        FakeImportBatchRepository importBatches = new();
        importBatches.Seed(new Quotinator.Data.Entities.ImportBatchEntity { Id = Guid.Parse(LiveBatch), Name = "live.json" });

        NotificationActionAvailability availability = await CreateExecutor(importBatches).GetAvailabilityAsync();

        Assert.IsTrue(availability.ImportBatchExists(LiveBatch.ToUpperInvariant()),
            "A batch that exists is reported, whatever casing it is asked about in.");
        Assert.IsFalse(availability.ImportBatchExists(GoneBatch), "A batch that does not exist is not.");
    }

    // ── #348: each option offered only while it can run ─────────────────────────────────────────────

    /// <summary>A reader over a folder that does not exist: no backups at all.</summary>
    private static DatabaseBackupReader NoBackups() => BackupsIn(Path.Combine(Path.GetTempPath(), "quotinator-348-none-" + Guid.NewGuid().ToString("N")));

    private static DatabaseBackupReader BackupsIn(string folder, int maxBackupStorageGb = 1) => new(
        new DatabaseOptions { DbPath = Path.Combine(folder, "quotinatordata.db"), BackupsPath = folder, MaxBackupStorageGb = maxBackupStorageGb },
        NoOpDiskSpaceProvider.Instance);

    private static IReadOnlyList<NotificationActionOption> ReseedOptions(BackupOutcome readiness, BackupOutcome? withOldestRemoved = null) =>
        CreateExecutor().AvailableOptions(NotificationDismissTrigger.Reseed, metadata: null, new NotificationActionAvailability([], readiness, withOldestRemoved));

    /// <summary>Every obstacle the pre-flight check itself can report.</summary>
    public static IEnumerable<object[]> PreflightObstacles =>
    [
        [BackupOutcome.BudgetExceeded], [BackupOutcome.InsufficientDiskSpace],
        [BackupOutcome.DestinationDirectoryNotWritable], [BackupOutcome.DestinationFileNotWritable],
    ];

    [TestMethod]
    public void BackUpThenReseed_IsOffered_WhenABackupCanBeTaken()
        => Assert.Contains(NotificationActionOption.BackUpThenReseed, ReseedOptions(BackupOutcome.Succeeded));

    /// <summary>Offering it would present an option that then refuses, which is what requirement 7 forbids.</summary>
    [TestMethod]
    [DynamicData(nameof(PreflightObstacles))]
    public void BackUpThenReseed_IsWithheld_WhenABackupCannotBeTaken(BackupOutcome obstacle)
        => Assert.DoesNotContain(NotificationActionOption.BackUpThenReseed, ReseedOptions(obstacle));

    [TestMethod]
    [DataRow(BackupOutcome.BudgetExceeded)]
    [DataRow(BackupOutcome.InsufficientDiskSpace)]
    public void RemoveOldestBackupThenReseed_IsOffered_WhenRemovingTheOldestClearsTheObstacle(BackupOutcome obstacle)
        => Assert.Contains(NotificationActionOption.RemoveOldestBackupThenReseed, ReseedOptions(obstacle, withOldestRemoved: BackupOutcome.Succeeded));

    /// <summary>Deleting a backup the user may want, for a backup that still could not be taken, is loss for nothing.</summary>
    [TestMethod]
    public void RemoveOldestBackupThenReseed_IsWithheld_WhenRemovingTheOldestWouldNotClearIt()
        => Assert.DoesNotContain(NotificationActionOption.RemoveOldestBackupThenReseed, ReseedOptions(BackupOutcome.BudgetExceeded, withOldestRemoved: BackupOutcome.BudgetExceeded));

    [TestMethod]
    public void RemoveOldestBackupThenReseed_IsWithheld_WhenThereIsNoBackupToRemove()
        => Assert.DoesNotContain(NotificationActionOption.RemoveOldestBackupThenReseed, ReseedOptions(BackupOutcome.BudgetExceeded, withOldestRemoved: null));

    /// <summary>With room already there, removing a backup would delete one for nothing.</summary>
    [TestMethod]
    public void RemoveOldestBackupThenReseed_IsWithheld_WhenABackupCanAlreadyBeTaken()
        => Assert.DoesNotContain(NotificationActionOption.RemoveOldestBackupThenReseed, ReseedOptions(BackupOutcome.Succeeded, withOldestRemoved: BackupOutcome.Succeeded));

    [TestMethod]
    [DynamicData(nameof(PreflightObstacles))]
    public void ReseedWithoutBackup_IsOffered_WhenABackupCannotBeTaken(BackupOutcome obstacle)
        => Assert.Contains(NotificationActionOption.ReseedWithoutBackup, ReseedOptions(obstacle));

    /// <summary>Running without a restore point when one can be had is a risk nobody needs to be offered.</summary>
    [TestMethod]
    public void ReseedWithoutBackup_IsWithheld_WhenABackupCanBeTaken()
        => Assert.DoesNotContain(NotificationActionOption.ReseedWithoutBackup, ReseedOptions(BackupOutcome.Succeeded));

    [TestMethod]
    public void ResetDatabase_IsOffered_WhenABackupCanBeTaken()
        => Assert.Contains(NotificationActionOption.ResetDatabase, CreateExecutor().AvailableOptions(
            NotificationDismissTrigger.DatabaseReset, metadata: null, new NotificationActionAvailability([], BackupOutcome.Succeeded)));

    /// <summary>A Reset refuses without a backup, so offering it then is offering a refusal.</summary>
    [TestMethod]
    [DynamicData(nameof(PreflightObstacles))]
    public void ResetDatabase_IsWithheld_WhenABackupCannotBeTaken(BackupOutcome obstacle)
        => Assert.DoesNotContain(NotificationActionOption.ResetDatabase, CreateExecutor().AvailableOptions(
            NotificationDismissTrigger.DatabaseReset, metadata: null, new NotificationActionAvailability([], obstacle)));

    [TestMethod]
    [DataRow(NotificationActionOption.KeepExisting)]
    [DataRow(NotificationActionOption.TakeIncoming)]
    public void ImportReviewOption_IsOffered_WhileItsBatchExists(NotificationActionOption option)
        => Assert.Contains(option, CreateExecutor().AvailableOptions(
            NotificationDismissTrigger.ImportReviewResolved, ReviewPayload(LiveBatch), new NotificationActionAvailability([LiveBatch])));

    [TestMethod]
    [DataRow(NotificationActionOption.KeepExisting)]
    [DataRow(NotificationActionOption.TakeIncoming)]
    public void ImportReviewOption_IsWithheld_OnceItsBatchIsGone(NotificationActionOption option)
        => Assert.DoesNotContain(option, CreateExecutor().AvailableOptions(
            NotificationDismissTrigger.ImportReviewResolved, ReviewPayload(GoneBatch), new NotificationActionAvailability([LiveBatch])));

    /// <summary>The page reads the pre-flight once per render, and every row's options follow from it.</summary>
    [TestMethod]
    public async Task GetAvailabilityAsync_ReportsWhetherABackupCanBeTakenNow()
    {
        NotificationActionExecutor executor = CreateExecutor(dbInitializer: new SpyDatabaseInitializer { Readiness = BackupOutcome.BudgetExceeded });

        Assert.AreEqual(BackupOutcome.BudgetExceeded, (await executor.GetAvailabilityAsync()).BackupReadiness);
    }

    /// <summary>
    /// It weighs the <em>oldest</em> backup: the pre-flight answers <c>Succeeded</c> only when handed that
    /// backup's exact size, so weighing any other file, or none, reads differently.
    /// </summary>
    [TestMethod]
    public async Task GetAvailabilityAsync_WeighsRemovingTheOldestBackup()
    {
        string folder = Path.Combine(Path.GetTempPath(), "quotinator-348-oldest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string oldest = Path.Combine(folder, "quotinatordata_backup_v1_20260901T000000Z.db");
            string newest = Path.Combine(folder, "quotinatordata_backup_v1_20260902T000000Z.db");
            File.WriteAllBytes(oldest, new byte[300]);
            File.WriteAllBytes(newest, new byte[500]);
            File.SetLastWriteTimeUtc(oldest, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(newest, new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc));

            SpyDatabaseInitializer dbInitializer = new()
            {
                Readiness          = BackupOutcome.BudgetExceeded,
                ReadinessWhenFreed = freed => freed == 300 ? BackupOutcome.Succeeded : BackupOutcome.BudgetExceeded,
            };

            NotificationActionAvailability availability =
                await CreateExecutor(dbInitializer: dbInitializer, backupReader: BackupsIn(folder)).GetAvailabilityAsync();

            Assert.AreEqual(BackupOutcome.Succeeded, availability.BackupReadinessWithOldestRemoved);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// At the quota a backup runs inside the reserve, and may reach the ceiling while it is taken since its
    /// size is not known in advance, so the user is told before choosing an option that takes one (#348,
    /// developer 2026-09-26). 95% of a 1 GB ceiling against the 90% default quota.
    /// </summary>
    [TestMethod]
    public async Task GetAvailabilityAsync_AtTheQuota_CautionsTheBackup()
    {
        string folder = Path.Combine(Path.GetTempPath(), "quotinator-348-caution-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            using (FileStream filler = new(Path.Combine(folder, "filler.db"), FileMode.Create, FileAccess.Write))
                filler.SetLength(1_073_741_824L * 95 / 100);

            NotificationActionAvailability availability = await CreateExecutor(backupReader: BackupsIn(folder)).GetAvailabilityAsync();

            Assert.IsTrue(availability.BackupCaution);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public async Task GetAvailabilityAsync_BelowTheQuota_DoesNotCautionTheBackup()
    {
        NotificationActionAvailability availability = await CreateExecutor(backupReader: NoBackups()).GetAvailabilityAsync();

        Assert.IsFalse(availability.BackupCaution);
    }

    /// <summary>
    /// #348: an option can be several operations, and re-verification belongs at the end of the action
    /// rather than after each one. Removing the oldest backup and then taking one evaluated the checks
    /// twice before this, which is the shape a boolean "it ran" cannot distinguish from once.
    /// </summary>
    [TestMethod]
    [DataRow(NotificationActionOption.BackUpThenReseed)]
    [DataRow(NotificationActionOption.RemoveOldestBackupThenReseed)]
    [DataRow(NotificationActionOption.ReseedWithoutBackup)]
    public async Task Reseed_EveryOption_EvaluatesTheConditionsOnce(NotificationActionOption option)
    {
        RecordingConditionCheck check = new();
        string folder = Path.Combine(Path.GetTempPath(), "quotinator-348-once-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "quotinatordata_v1_20260101T000000000Z.db"), "old");

        try
        {
            await CreateExecutor(backupsFolder: folder, conditionChecks: new NotificationConditionChecks([check], NullLogger<NotificationConditionChecks>.Instance))
                .ExecuteAsync(NotificationDismissTrigger.Reseed, option: option);

            Assert.AreEqual(1, check.Runs);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    /// <summary>Counts how often it ran; changes nothing.</summary>
    private sealed class RecordingConditionCheck : INotificationConditionCheck
    {
        /// <summary>How many times <see cref="CheckAsync"/> was called. A count, not a flag: the defect
        /// this guards against is an action evaluating the checks more than once, which "it ran" cannot see.</summary>
        public int Runs { get; private set; }

        /// <inheritdoc/>
        public NotificationMetadataKind Kind => NotificationMetadataKind.BackupQuotaReached;

        /// <inheritdoc/>
        public Task<NotificationConditionOutcome> CheckAsync()
        {
            Runs++;
            return Task.FromResult(NotificationConditionOutcome.Unchanged);
        }
    }

    // ── #348: the reseed action backs up first ──────────────────────────────────────────────────────

    private static SpyDatabaseInitializer BackupFails(BackupOutcome obstacle) => new() { BackupResult = DatabaseBackupResult.Failed(obstacle), Readiness = obstacle };

    [TestMethod]
    public async Task Reseed_BackUpThenReseed_TakesABackupBeforeReseeding()
    {
        SpyDatabaseInitializer db = new();

        await CreateExecutor(dbInitializer: db).ExecuteAsync(NotificationDismissTrigger.Reseed, option: NotificationActionOption.BackUpThenReseed);

        Assert.AreSequenceEqual(["backup", "reseed"], db.Calls);
    }

    /// <summary>A backup that fails after the check passed stops the reseed: going on would be reseeding without one, unasked.</summary>
    [TestMethod]
    public async Task Reseed_BackUpThenReseed_WhenTheBackupFails_DoesNotReseed()
    {
        SpyDatabaseInitializer db = BackupFails(BackupOutcome.DiskFilledDuringBackup);

        await CreateExecutor(dbInitializer: db).ExecuteAsync(NotificationDismissTrigger.Reseed, option: NotificationActionOption.BackUpThenReseed);

        Assert.IsFalse(db.ReseedCalled);
    }

    [TestMethod]
    public async Task Reseed_BackUpThenReseed_WhenTheBackupFails_LeavesTheNotificationActive()
    {
        FakeNotificationWriter writer = new();

        await CreateExecutor(dbInitializer: BackupFails(BackupOutcome.DiskFilledDuringBackup), notificationWriter: writer)
            .ExecuteAsync(NotificationDismissTrigger.Reseed, option: NotificationActionOption.BackUpThenReseed);

        Assert.IsEmpty(writer.DismissByTriggerCalls);
    }

    [TestMethod]
    public async Task Reseed_BackUpThenReseed_WhenTheBackupFails_ReportsTheObstacle()
    {
        NotificationActionResult result = await CreateExecutor(dbInitializer: BackupFails(BackupOutcome.DiskFilledDuringBackup))
            .ExecuteAsync(NotificationDismissTrigger.Reseed, option: NotificationActionOption.BackUpThenReseed);

        Assert.AreEqual(BackupOutcome.DiskFilledDuringBackup, result.BackupObstacle);
    }

    /// <summary>The backup the reseed takes is recorded like one an operator asked for, since it is one.</summary>
    [TestMethod]
    public async Task Reseed_BackUpThenReseed_RecordsTheBackupInTheAuditTrail()
    {
        RecordingAuditEntryWriter audit = new();

        await CreateExecutor(auditWriter: audit).ExecuteAsync(NotificationDismissTrigger.Reseed, option: NotificationActionOption.BackUpThenReseed);

        Assert.Contains(AuditOperation.Backup, audit.Operations);
    }

    /// <summary>With no option named, a reseed takes the one path that never gives up a restore point.</summary>
    [TestMethod]
    public async Task Reseed_WithNoOptionNamed_TakesABackupBeforeReseeding()
    {
        SpyDatabaseInitializer db = new();

        await CreateExecutor(dbInitializer: db).ExecuteAsync(NotificationDismissTrigger.Reseed);

        Assert.AreSequenceEqual(["backup", "reseed"], db.Calls);
    }

    [TestMethod]
    public async Task Reseed_RemoveOldestBackupThenReseed_RemovesTheOldestBackup()
    {
        (string folder, string oldest, _) = TwoBackups();
        try
        {
            await CreateExecutor(backupsFolder: folder).ExecuteAsync(NotificationDismissTrigger.Reseed, option: NotificationActionOption.RemoveOldestBackupThenReseed);

            Assert.IsFalse(File.Exists(oldest));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    /// <summary>Only the one backup the option names goes: the rest are restore points the user did not agree to lose.</summary>
    [TestMethod]
    public async Task Reseed_RemoveOldestBackupThenReseed_KeepsTheNewerBackup()
    {
        (string folder, _, string newest) = TwoBackups();
        try
        {
            await CreateExecutor(backupsFolder: folder).ExecuteAsync(NotificationDismissTrigger.Reseed, option: NotificationActionOption.RemoveOldestBackupThenReseed);

            Assert.IsTrue(File.Exists(newest));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [TestMethod]
    public async Task Reseed_RemoveOldestBackupThenReseed_RecordsTheRemovalInTheAuditTrail()
    {
        (string folder, _, _) = TwoBackups();
        RecordingAuditEntryWriter audit = new();
        try
        {
            await CreateExecutor(backupsFolder: folder, auditWriter: audit).ExecuteAsync(NotificationDismissTrigger.Reseed, option: NotificationActionOption.RemoveOldestBackupThenReseed);

            Assert.Contains(AuditOperation.BackupDeleted, audit.Operations);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [TestMethod]
    public async Task Reseed_RemoveOldestBackupThenReseed_TakesABackupBeforeReseeding()
    {
        (string folder, _, _) = TwoBackups();
        SpyDatabaseInitializer db = new();
        try
        {
            await CreateExecutor(dbInitializer: db, backupsFolder: folder).ExecuteAsync(NotificationDismissTrigger.Reseed, option: NotificationActionOption.RemoveOldestBackupThenReseed);

            Assert.AreSequenceEqual(["backup", "reseed"], db.Calls);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    /// <summary>
    /// A backup that exists but cannot be removed (a read-only folder) leaves no room for the backup this
    /// option relies on, so the reseed does not run.
    /// </summary>
    [TestMethod]
    public async Task Reseed_RemoveOldestBackupThenReseed_WhenTheRemovalFails_DoesNotReseed()
    {
        (string folder, _, _) = TwoBackups();
        SpyDatabaseInitializer db = new();
        try
        {
            await CreateExecutor(dbInitializer: db, backupsFolder: folder, backupWriter: new RefusingBackupWriter())
                .ExecuteAsync(NotificationDismissTrigger.Reseed, option: NotificationActionOption.RemoveOldestBackupThenReseed);

            Assert.IsFalse(db.ReseedCalled);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    /// <summary>Refuses every removal, as a read-only backups folder does.</summary>
    private sealed class RefusingBackupWriter : IDatabaseBackupWriter
    {
        public BackupDeleteOutcome Delete(string name) => BackupDeleteOutcome.NotRemovable;
    }

    /// <summary>The user's permission is what the option carries, so with it the reseed runs although no backup can be taken.</summary>
    [TestMethod]
    public async Task Reseed_ReseedWithoutBackup_Reseeds()
    {
        SpyDatabaseInitializer db = BackupFails(BackupOutcome.BudgetExceeded);

        await CreateExecutor(dbInitializer: db).ExecuteAsync(NotificationDismissTrigger.Reseed, option: NotificationActionOption.ReseedWithoutBackup);

        Assert.IsTrue(db.ReseedCalled);
    }

    [TestMethod]
    public async Task Reseed_ReseedWithoutBackup_AttemptsNoBackup()
    {
        SpyDatabaseInitializer db = BackupFails(BackupOutcome.BudgetExceeded);

        await CreateExecutor(dbInitializer: db).ExecuteAsync(NotificationDismissTrigger.Reseed, option: NotificationActionOption.ReseedWithoutBackup);

        Assert.DoesNotContain("backup", db.Calls);
    }

    /// <summary>A reseed with no restore point is recorded where a later "where is the backup" question finds the answer.</summary>
    [TestMethod]
    public async Task Reseed_ReseedWithoutBackup_RecordsTheSkippedBackupInTheAuditTrail()
    {
        RecordingAuditEntryWriter audit = new();

        await CreateExecutor(dbInitializer: BackupFails(BackupOutcome.BudgetExceeded), auditWriter: audit)
            .ExecuteAsync(NotificationDismissTrigger.Reseed, option: NotificationActionOption.ReseedWithoutBackup);

        Assert.Contains(AuditOperation.BackupSkipped, audit.Operations);
    }

    /// <summary>Rendered through Serilog, per docs/logging.md, so the assertion reads what the real log does.</summary>
    [TestMethod]
    public async Task Reseed_ReseedWithoutBackup_LogsTheSkippedBackup()
    {
        CaptureSink sink = new();
        using Serilog.Core.Logger serilog = new Serilog.LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        ILogger<NotificationActionExecutor> logger = new Serilog.Extensions.Logging.SerilogLoggerFactory(serilog).CreateLogger<NotificationActionExecutor>();

        await CreateExecutor(dbInitializer: BackupFails(BackupOutcome.BudgetExceeded), logger: logger)
            .ExecuteAsync(NotificationDismissTrigger.Reseed, option: NotificationActionOption.ReseedWithoutBackup);

        Assert.Contains(m => m.Contains("reseed proceeding WITHOUT a backup (BudgetExceeded)", StringComparison.Ordinal), sink.Lines);
    }

    /// <summary>Found 2026-09-26: a refused Reset was treated as a completed one, marking a still-broken database healthy.</summary>
    [TestMethod]
    public async Task DatabaseReset_WhenRefused_LeavesTheDatabaseUnhealthy()
    {
        DatabaseHealthState health = new();
        health.MarkFailed("some prior failure");

        await CreateExecutor(dbInitializer: new SpyDatabaseInitializer { ResetResult = DatabaseOperationResult.RefusedForBackup(BackupOutcome.BudgetExceeded, BackupGuardedStep.Reset) }, health: health)
            .ExecuteAsync(NotificationDismissTrigger.DatabaseReset);

        Assert.IsFalse(health.IsHealthy);
    }

    [TestMethod]
    public async Task DatabaseReset_WhenRefused_LeavesTheNotificationActive()
    {
        FakeNotificationWriter writer = new();

        await CreateExecutor(dbInitializer: new SpyDatabaseInitializer { ResetResult = DatabaseOperationResult.RefusedForBackup(BackupOutcome.BudgetExceeded, BackupGuardedStep.Reset) }, notificationWriter: writer)
            .ExecuteAsync(NotificationDismissTrigger.DatabaseReset);

        Assert.IsEmpty(writer.DismissByTriggerCalls);
    }

    [TestMethod]
    public async Task DatabaseReset_WhenRefused_ReportsTheObstacle()
    {
        NotificationActionResult result = await CreateExecutor(dbInitializer: new SpyDatabaseInitializer { ResetResult = DatabaseOperationResult.RefusedForBackup(BackupOutcome.BudgetExceeded, BackupGuardedStep.Reset) })
            .ExecuteAsync(NotificationDismissTrigger.DatabaseReset);

        Assert.AreEqual(BackupOutcome.BudgetExceeded, result.BackupObstacle);
    }

    /// <summary>Two backups a day apart in a folder of their own: the older one is the one to remove.</summary>
    private static (string Folder, string Oldest, string Newest) TwoBackups()
    {
        string folder = Path.Combine(Path.GetTempPath(), "quotinator-348-two-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string oldest = Path.Combine(folder, "quotinatordata_backup_v1_20260901T000000Z.db");
        string newest = Path.Combine(folder, "quotinatordata_backup_v1_20260902T000000Z.db");
        File.WriteAllBytes(oldest, new byte[300]);
        File.WriteAllBytes(newest, new byte[500]);
        File.SetLastWriteTimeUtc(oldest, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newest, new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc));
        return (folder, oldest, newest);
    }

    /// <summary>Records every audit entry's operation, so a test can ask what was recorded.</summary>
    private sealed class RecordingAuditEntryWriter : IAuditEntryWriter
    {
        public List<string> Operations { get; } = [];

        public Task WriteAsync(AuditEntryEntity entry, System.Data.IDbConnection connection, System.Data.IDbTransaction? transaction = null) => WriteAsync(entry);

        public Task WriteAsync(IReadOnlyList<AuditEntryEntity> entries, System.Data.IDbConnection connection, System.Data.IDbTransaction? transaction = null)
        {
            Operations.AddRange(entries.Select(e => e.Operation));
            return Task.CompletedTask;
        }

        public Task WriteAsync(AuditEntryEntity entry)
        {
            Operations.Add(entry.Operation);
            return Task.CompletedTask;
        }

        public Task ClearAsync(string? table = null) => Task.CompletedTask;
    }

    /// <summary>Captures what <see cref="INotificationActionExecutor.ExecuteAsync"/> was handed, without performing real work.</summary>
    private sealed class RecordingExecutor : INotificationActionExecutor
    {
        public NotificationMetadataDto? ReceivedMetadata { get; private set; }

        public bool CanExecute(NotificationDismissTrigger trigger) => true;

        public bool CanExecute(NotificationDismissTrigger trigger, NotificationMetadataDto? metadata, NotificationActionAvailability availability) => true;

        public IReadOnlyList<NotificationActionOption> AvailableOptions(NotificationDismissTrigger trigger, NotificationMetadataDto? metadata, NotificationActionAvailability availability) => [];

        public Task<NotificationActionAvailability> GetAvailabilityAsync() => Task.FromResult(new NotificationActionAvailability([]));

        /// <summary>The choice the caller passed, for a trigger that offers more than one outcome (#303).</summary>
        public FieldResolutionChoice? ReceivedChoice { get; private set; }

        public Task<NotificationActionResult> ExecuteAsync(
            NotificationDismissTrigger trigger, NotificationMetadataDto? metadata = null,
            FieldResolutionChoice? choice = null, NotificationActionOption? option = null)
        {
            ReceivedMetadata = metadata;
            ReceivedChoice   = choice;
            return Task.FromResult(NotificationActionResult.Success());
        }
    }

    private sealed class SpyDatabaseInitializer : IDatabaseInitializer
    {
        public bool ResetCalled { get; private set; }

        public int SchemaVersion => 0;
        public int DataSchemaVersion => 0;
        public int QuoteCount => 0;
        public int SourceCount => 0;
        public int CharacterCount => 0;
        public int PeopleCount => 0;
        public int SeriesCount => 0;
        public int SeasonCount => 0;
        public int UniverseCount => 0;
        public int StageDirectionCount => 0;
        public int SoundCueCount => 0;
        public int ConversationCount => 0;
        public string? MigrationApplied => null;
        public bool SchemaVersionOvershootDetected => false;
        public IReadOnlyList<FileImportReport> LastSeedReport => [];

        public Task<DatabaseOperationResult> InitialiseAsync() => Task.FromResult(DatabaseOperationResult.Success());

        /// <summary>What the pre-flight answers when nothing is freed first.</summary>
        public BackupOutcome Readiness { get; init; } = BackupOutcome.Succeeded;

        /// <summary>What it answers when bytes are freed first; the number freed decides, so a test can tell which backup was weighed.</summary>
        public Func<long, BackupOutcome>? ReadinessWhenFreed { get; init; }

        public BackupOutcome CheckBackupReadiness(long bytesFreedFirst = 0) =>
            bytesFreedFirst > 0 && ReadinessWhenFreed is not null ? ReadinessWhenFreed(bytesFreedFirst) : Readiness;
        /// <summary>Every backup and reseed, in the order they happened.</summary>
        public List<string> Calls { get; } = [];

        /// <summary>What a backup attempt reports.</summary>
        public DatabaseBackupResult BackupResult { get; init; } = DatabaseBackupResult.Success("spy-backup.db");

        /// <summary>What a Reset reports.</summary>
        public DatabaseOperationResult ResetResult { get; init; } = DatabaseOperationResult.Success();

        public Task<DatabaseBackupResult> CreateBackupAsync()
        {
            Calls.Add("backup");
            return Task.FromResult(BackupResult);
        }
        public bool ReseedCalled { get; private set; }

        public bool? ReseedForcedSourceRefresh { get; private set; }

        public Task ReseedAsync(bool forceSourceRefresh = false)
        {
            Calls.Add("reseed");
            ReseedCalled = true;
            ReseedForcedSourceRefresh = forceSourceRefresh;
            return Task.CompletedTask;
        }

        public Task<DatabaseOperationResult> ResetAsync(bool preserveSchemaVersion = false, bool forceSourceRefresh = false, bool allowNoBackup = false)
        {
            ResetCalled = true;
            return Task.FromResult(ResetResult);
        }

        public Task<SeedPreviewResult> PreviewSeedAsync() => Task.FromResult(new SeedPreviewResult([], []));
        public Task<SourceCacheResolution> RefreshSourcesAsync(bool force = false) => Task.FromResult(new SourceCacheResolution([], []));
    }
}

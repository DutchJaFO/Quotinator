using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Api.Services;
using Quotinator.Api.Startup;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Enums;
using Quotinator.Data.Import;
using Quotinator.Data.Notifications;
using Quotinator.Data.Repositories;

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
        /// <summary>Every recorded pair, in call order — kept as the pair, since #312 made the pair the identity.</summary>
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
            new SpyAppVersionTracker(), new FakeVersionService(), NullLogger<NotificationActionExecutor>.Instance, importActions);

        await executor.ExecuteAsync(
            NotificationDismissTrigger.ImportReviewResolved, ReviewPayload(batchId), FieldResolutionChoice.Keep);

        Assert.Contains((batchId, FieldResolutionChoice.Keep), importActions.DecideBatchCalls,
            "The alert's own payload names the batch, so the action resolves that batch and no other.");
    }

    /// <summary>
    /// The alert's remedy applies the batch it decided. Found in T2 (2026-09-01): the executor decided
    /// and stopped, leaving every action <c>Decided</c> and never <c>Applied</c> — so the operator's
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
            new SpyAppVersionTracker(), new FakeVersionService(), NullLogger<NotificationActionExecutor>.Instance, importActions);

        await executor.ExecuteAsync(
            NotificationDismissTrigger.ImportReviewResolved, ReviewPayload(batchId), FieldResolutionChoice.Replace);

        Assert.AreEqual(batchId, importActions.LastAppliedBatchId,
            "A decision that is never applied changes nothing and leaves the alert active.");
    }

    /// <summary>
    /// Nothing is applied when no choice was given — the throw must happen before any write, or a
    /// rejected request would still have moved the batch on.
    /// </summary>
    [TestMethod]
    public async Task ImportReviewResolved_WithoutAChoice_AppliesNothing()
    {
        FakeImportActionService importActions = new();
        NotificationActionExecutor executor = new(
            new SpyDatabaseInitializer(), new DatabaseHealthState(), new FakeNotificationWriter(),
            new SpyAppVersionTracker(), new FakeVersionService(), NullLogger<NotificationActionExecutor>.Instance, importActions);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(NotificationDismissTrigger.ImportReviewResolved, ReviewPayload(Guid.NewGuid().ToString("D"))));

        Assert.IsNull(importActions.LastAppliedBatchId);
    }

    /// <summary>
    /// No default side. Choosing one on the operator's behalf would silently overwrite their data with
    /// whichever way the code happened to lean — keeping and replacing are not interchangeable.
    /// </summary>
    [TestMethod]
    public async Task ImportReviewResolved_WithoutAChoice_Throws()
    {
        FakeImportActionService importActions = new();
        NotificationActionExecutor executor = new(
            new SpyDatabaseInitializer(), new DatabaseHealthState(), new FakeNotificationWriter(),
            new SpyAppVersionTracker(), new FakeVersionService(), NullLogger<NotificationActionExecutor>.Instance, importActions);

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
            new SpyAppVersionTracker(), new FakeVersionService(), NullLogger<NotificationActionExecutor>.Instance, importActions);

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
            new SpyAppVersionTracker(), new FakeVersionService(), NullLogger<NotificationActionExecutor>.Instance, new FakeImportActionService());

        Assert.IsTrue(executor.CanExecute(NotificationDismissTrigger.ImportReviewResolved));
    }

    [TestMethod]
    public void CanExecute_DatabaseReset_ReturnsTrue()
    {
        NotificationActionExecutor executor = new(
            new SpyDatabaseInitializer(), new DatabaseHealthState(), new FakeNotificationWriter(),
            new SpyAppVersionTracker(), new FakeVersionService(), NullLogger<NotificationActionExecutor>.Instance, new FakeImportActionService());

        Assert.IsTrue(executor.CanExecute(NotificationDismissTrigger.DatabaseReset));
    }

    /// <summary>#304: the Reseed trigger is executable, so `NotificationTable` renders its Run → Confirm control.</summary>
    [TestMethod]
    public void CanExecute_Reseed_ReturnsTrue()
    {
        NotificationActionExecutor executor = new(
            new SpyDatabaseInitializer(), new DatabaseHealthState(), new FakeNotificationWriter(),
            new SpyAppVersionTracker(), new FakeVersionService(), NullLogger<NotificationActionExecutor>.Instance, new FakeImportActionService());

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
            new SpyAppVersionTracker(), new FakeVersionService(), NullLogger<NotificationActionExecutor>.Instance, new FakeImportActionService());

        await executor.ExecuteAsync(NotificationDismissTrigger.Reseed);

        Assert.IsTrue(dbInitializer.ReseedCalled);
        Assert.IsFalse(dbInitializer.ReseedForcedSourceRefresh,
            "The content is already downloaded by the time the recommendation exists — forcing another "
            + "network round-trip would be redundant.");
        Assert.AreSequenceEqual([NotificationDismissTrigger.Reseed], notificationWriter.DismissByTriggerCalls);

        // #308, the third defect T2 found: the dismissal happened and carried no resolution, so the row
        // read Done while saying nothing about what settled it. Asserting the trigger alone passed
        // throughout — the fake was discarding the resolution argument entirely.
        Assert.AreSequenceEqual([NotificationResolution.Reseeded], notificationWriter.DismissByTriggerResolutions,
            "A reseed run from its own notification must record that a reseed is what resolved it.");
    }

    /// <summary>
    /// #304: the Reseed case deliberately does *not* copy two steps from the DatabaseReset case beside
    /// it. A reseed replaces content within an intact schema — it neither degrades health nor empties
    /// System_AppVersion — so marking healthy or re-recording the version would assert a recovery that
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
            appVersionTracker, new FakeVersionService(), NullLogger<NotificationActionExecutor>.Instance, new FakeImportActionService());

        await executor.ExecuteAsync(NotificationDismissTrigger.Reseed);

        Assert.IsFalse(health.IsHealthy,
            "A reseed says nothing about whether a prior failure was resolved — only Reset rebuilds the schema.");
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
            dbInitializer, health, notificationWriter, appVersionTracker, new FakeVersionService(), NullLogger<NotificationActionExecutor>.Instance, new FakeImportActionService());

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
    /// it — a schema-version overshoot is resolved for the whole database, so there is nothing to narrow
    /// — but the channel has to be proven to carry the value, or #304's Reseed inherits an untested seam
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
            "The payload must arrive at the executor unchanged — not re-serialized, and not dropped.");
        Assert.IsFalse(dbInitializer.ResetCalled, "Sanity check: this test drives the recording double, not the real executor.");
    }

    /// <summary>A notification with no payload still executes — every row written before #312 is this case.</summary>
    [TestMethod]
    public async Task ExecuteAsync_WithoutMetadata_StillPerformsTheAction()
    {
        SpyDatabaseInitializer dbInitializer = new();
        DatabaseHealthState health = new();
        NotificationActionExecutor executor = new(
            dbInitializer, health, new FakeNotificationWriter(), new SpyAppVersionTracker(),
            new FakeVersionService(), NullLogger<NotificationActionExecutor>.Instance, new FakeImportActionService());

        await executor.ExecuteAsync(NotificationDismissTrigger.DatabaseReset);

        Assert.IsTrue(dbInitializer.ResetCalled);
    }

    /// <summary>Captures what <see cref="INotificationActionExecutor.ExecuteAsync"/> was handed, without performing real work.</summary>
    private sealed class RecordingExecutor : INotificationActionExecutor
    {
        public NotificationMetadataDto? ReceivedMetadata { get; private set; }

        public bool CanExecute(NotificationDismissTrigger trigger) => true;

        /// <summary>The choice the caller passed, for a trigger that offers more than one outcome (#303).</summary>
        public FieldResolutionChoice? ReceivedChoice { get; private set; }

        public Task ExecuteAsync(NotificationDismissTrigger trigger, NotificationMetadataDto? metadata = null, FieldResolutionChoice? choice = null)
        {
            ReceivedMetadata = metadata;
            ReceivedChoice   = choice;
            return Task.CompletedTask;
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
        public int UniverseCount => 0;
        public int StageDirectionCount => 0;
        public int SoundCueCount => 0;
        public int ConversationCount => 0;
        public string? MigrationApplied => null;
        public bool SchemaVersionOvershootDetected => false;
        public IReadOnlyList<FileImportReport> LastSeedReport => [];

        public Task<DatabaseOperationResult> InitialiseAsync() => Task.FromResult(DatabaseOperationResult.Success());

        public BackupOutcome CheckBackupReadiness(bool allowReserve = false) => BackupOutcome.Succeeded;
        public Task<DatabaseBackupResult> CreateBackupAsync() => Task.FromResult(DatabaseBackupResult.Success("spy-backup.db"));
        public bool ReseedCalled { get; private set; }

        public bool? ReseedForcedSourceRefresh { get; private set; }

        public Task ReseedAsync(bool forceSourceRefresh = false)
        {
            ReseedCalled = true;
            ReseedForcedSourceRefresh = forceSourceRefresh;
            return Task.CompletedTask;
        }

        public Task<DatabaseOperationResult> ResetAsync(bool preserveSchemaVersion = false, bool forceSourceRefresh = false, bool allowNoBackup = false)
        {
            ResetCalled = true;
            return Task.FromResult(DatabaseOperationResult.Success());
        }

        public Task<SeedPreviewResult> PreviewSeedAsync() => Task.FromResult(new SeedPreviewResult([], []));
        public Task<SourceCacheResolution> RefreshSourcesAsync(bool force = false) => Task.FromResult(new SourceCacheResolution([], []));
    }
}

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

    [TestMethod]
    public void CanExecute_DatabaseReset_ReturnsTrue()
    {
        NotificationActionExecutor executor = new(
            new SpyDatabaseInitializer(), new DatabaseHealthState(), new FakeNotificationWriter(),
            new SpyAppVersionTracker(), new FakeVersionService(), NullLogger<NotificationActionExecutor>.Instance);

        Assert.IsTrue(executor.CanExecute(NotificationDismissTrigger.DatabaseReset));
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
            dbInitializer, health, notificationWriter, appVersionTracker, new FakeVersionService(), NullLogger<NotificationActionExecutor>.Instance);

        await executor.ExecuteAsync(NotificationDismissTrigger.DatabaseReset);

        Assert.IsTrue(dbInitializer.ResetCalled);
        Assert.IsTrue(health.IsHealthy);
        Assert.HasCount(1, notificationWriter.DismissByTriggerCalls);
        Assert.AreEqual(NotificationDismissTrigger.DatabaseReset, notificationWriter.DismissByTriggerCalls[0]);
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
            new FakeVersionService(), NullLogger<NotificationActionExecutor>.Instance);

        await executor.ExecuteAsync(NotificationDismissTrigger.DatabaseReset);

        Assert.IsTrue(dbInitializer.ResetCalled);
    }

    /// <summary>Captures what <see cref="INotificationActionExecutor.ExecuteAsync"/> was handed, without performing real work.</summary>
    private sealed class RecordingExecutor : INotificationActionExecutor
    {
        public NotificationMetadataDto? ReceivedMetadata { get; private set; }

        public bool CanExecute(NotificationDismissTrigger trigger) => true;

        public Task ExecuteAsync(NotificationDismissTrigger trigger, NotificationMetadataDto? metadata = null)
        {
            ReceivedMetadata = metadata;
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
        public Task ReseedAsync(bool forceSourceRefresh = false) => Task.CompletedTask;

        public Task<DatabaseOperationResult> ResetAsync(bool preserveSchemaVersion = false, bool forceSourceRefresh = false, bool allowNoBackup = false)
        {
            ResetCalled = true;
            return Task.FromResult(DatabaseOperationResult.Success());
        }

        public Task<SeedPreviewResult> PreviewSeedAsync() => Task.FromResult(new SeedPreviewResult([], []));
        public Task<SourceCacheResolution> RefreshSourcesAsync(bool force = false) => Task.FromResult(new SourceCacheResolution([], []));
    }
}

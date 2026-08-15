using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Api.Services;
using Quotinator.Api.Startup;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Enums;
using Quotinator.Data.Import;
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

        public Task InitialiseAsync() => Task.CompletedTask;
        public Task ReseedAsync(bool forceSourceRefresh = false) => Task.CompletedTask;

        public Task ResetAsync(bool preserveSchemaVersion = false, bool forceSourceRefresh = false)
        {
            ResetCalled = true;
            return Task.CompletedTask;
        }

        public Task<SeedPreviewResult> PreviewSeedAsync() => Task.FromResult(new SeedPreviewResult([], []));
        public Task<SourceCacheResolution> RefreshSourcesAsync(bool force = false) => Task.FromResult(new SourceCacheResolution([], []));
    }
}

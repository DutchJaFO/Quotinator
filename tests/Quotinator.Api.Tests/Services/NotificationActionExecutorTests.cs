using Quotinator.Api.Services;
using Quotinator.Api.Startup;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Data.Database;
using Quotinator.Data.Enums;
using Quotinator.Data.Import;

namespace Quotinator.Api.Tests.Services;

/// <summary>Exercises <see cref="NotificationActionExecutor"/> (#278).</summary>
[TestClass]
public class NotificationActionExecutorTests
{
    [TestMethod]
    public void CanExecute_DatabaseReset_ReturnsTrue()
    {
        var executor = new NotificationActionExecutor(new SpyDatabaseInitializer(), new DatabaseHealthState(), new FakeNotificationWriter());

        Assert.IsTrue(executor.CanExecute(NotificationDismissTrigger.DatabaseReset));
    }

    [TestMethod]
    public async Task ExecuteAsync_DatabaseReset_CallsResetAndMarksHealthyAndDismissesMatchingNotifications()
    {
        var dbInitializer = new SpyDatabaseInitializer();
        var health = new DatabaseHealthState();
        health.MarkFailed("some prior failure");
        var notificationWriter = new FakeNotificationWriter();
        var executor = new NotificationActionExecutor(dbInitializer, health, notificationWriter);

        await executor.ExecuteAsync(NotificationDismissTrigger.DatabaseReset);

        Assert.IsTrue(dbInitializer.ResetCalled);
        Assert.IsTrue(health.IsHealthy);
        Assert.HasCount(1, notificationWriter.DismissByTriggerCalls);
        Assert.AreEqual(NotificationDismissTrigger.DatabaseReset, notificationWriter.DismissByTriggerCalls[0]);
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

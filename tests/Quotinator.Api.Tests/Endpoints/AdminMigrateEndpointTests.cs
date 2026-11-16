using System.Data;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Startup;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Data.Notifications;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Api.Tests.Endpoints;

/// <summary>
/// #490 step 17 (developer, 2026-11-16): with no room there can be no backup, so a migration that needs one
/// degrades startup, and the operator either frees room or confirms migrating without one. The migrate endpoint
/// resumes it: with the backup when there is room, without one only when confirmed.
/// </summary>
[TestClass]
public sealed class AdminMigrateEndpointTests
{
    private const string TestKey = "test-admin-key";
    private const string Migrate = "/api/v1/admin/database/migrate";

    /// <summary>Startup with a migration refused for want of a backup degrades, and awaits the operator.</summary>
    [TestMethod]
    public async Task Startup_MigrationRefusedForBackup_DegradesAwaitingConfirmation()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(new MigrationSpyInitializer { RefuseMigrationWith = BackupOutcome.InsufficientDiskSpace });
        _ = factory.CreateClient();

        DatabaseHealthState health = factory.Services.GetRequiredService<DatabaseHealthState>();
        Assert.IsFalse(health.IsHealthy);
        Assert.IsTrue(health.MigrationAwaitsConfirmation);
    }

    /// <summary>Without the confirmation and still no room, the endpoint refuses, says why, and offers the confirmation.</summary>
    [TestMethod]
    public async Task Migrate_StillNoRoom_WithoutOverride_RefusesNamingTheObstacle()
    {
        MigrationSpyInitializer spy = new() { RefuseMigrationWith = BackupOutcome.InsufficientDiskSpace };
        using WebApplicationFactory<Program> factory = CreateFactory(spy);

        HttpResponseMessage response = await Client(factory).PostAsync(Migrate, null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        Assert.AreEqual(nameof(BackupOutcome.InsufficientDiskSpace), body.RootElement.GetProperty("backupObstacle").GetString());
        Assert.Contains("allowNoBackup", body.RootElement.GetProperty("remedies").GetRawText(), StringComparison.Ordinal);
        Assert.IsFalse(factory.Services.GetRequiredService<DatabaseHealthState>().IsHealthy, "A refused resume made the application healthy.");
    }

    /// <summary>The confirmation: the migration runs without a backup, the application is healthy, and the skip is recorded.</summary>
    [TestMethod]
    public async Task Migrate_WithOverride_MigratesWithoutABackupAndRecordsIt()
    {
        MigrationSpyInitializer spy = new() { RefuseMigrationWith = BackupOutcome.InsufficientDiskSpace };
        RecordingAuditWriter audit = new();
        using WebApplicationFactory<Program> factory = CreateFactory(spy, audit: audit);

        HttpResponseMessage response = await Client(factory).PostAsync(Migrate + "?allowNoBackup=true", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(spy.LastAllowNoBackup, "The confirmation did not reach the initialisation.");
        Assert.IsTrue(factory.Services.GetRequiredService<DatabaseHealthState>().IsHealthy);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        JsonElement backup = body.RootElement.GetProperty("backup");
        Assert.AreEqual("Skipped", backup.GetProperty("outcome").GetString());
        Assert.AreEqual(nameof(BackupOutcome.InsufficientDiskSpace), backup.GetProperty("obstacle").GetString());
        Assert.Contains(AuditOperation.BackupSkipped, audit.Operations, "The skipped backup was not recorded in the audit trail.");
    }

    /// <summary>With room freed since startup, the same call takes the backup, without the confirmation.</summary>
    [TestMethod]
    public async Task Migrate_RoomFreed_WithoutOverride_MigratesWithTheBackup()
    {
        MigrationSpyInitializer spy = new() { RefuseMigrationWith = BackupOutcome.InsufficientDiskSpace };
        using WebApplicationFactory<Program> factory = CreateFactory(spy);
        HttpClient client = Client(factory);
        spy.RefuseMigrationWith = null;

        HttpResponseMessage response = await client.PostAsync(Migrate, null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(factory.Services.GetRequiredService<DatabaseHealthState>().IsHealthy);
        Assert.IsFalse(spy.LastAllowNoBackup, "A call without the confirmation forwarded one.");
    }

    /// <summary>
    /// A resumed migration leaves what a healthy restart would: the startup work that runs only when healthy runs
    /// too, among it one notification per migration applied (#371).
    /// </summary>
    [TestMethod]
    public async Task Migrate_WithOverride_WritesTheMigrationAppliedNotifications()
    {
        MigrationSpyInitializer spy = new() { RefuseMigrationWith = BackupOutcome.InsufficientDiskSpace };
        FakeNotificationWriter notifications = new();
        using WebApplicationFactory<Program> factory = CreateFactory(spy, notifications: notifications);

        HttpResponseMessage response = await Client(factory).PostAsync(Migrate + "?allowNoBackup=true", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(m => m.Kind == NotificationMetadataKind.MigrationApplied, notifications.WrittenMetadata,
            "The resumed migration wrote no migration-applied notification.");
    }

    /// <summary>Only a refused migration can be resumed: on a healthy application there is nothing to resume.</summary>
    [TestMethod]
    public async Task Migrate_Healthy_RefusesWithNothingToResume()
    {
        MigrationSpyInitializer spy = new();
        using WebApplicationFactory<Program> factory = CreateFactory(spy);
        HttpClient client = Client(factory);
        int callsAtStartup = spy.InitialiseCalls;

        HttpResponseMessage response = await client.PostAsync(Migrate + "?allowNoBackup=true", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        Assert.AreEqual(callsAtStartup, spy.InitialiseCalls, "A healthy application initialised again.");
    }

    /// <summary>Control: like every admin endpoint, it needs the key.</summary>
    [TestMethod]
    public async Task Migrate_NoKey_Returns401()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(new MigrationSpyInitializer { RefuseMigrationWith = BackupOutcome.InsufficientDiskSpace });

        HttpResponseMessage response = await factory.CreateClient().PostAsync(Migrate, null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        MigrationSpyInitializer initializer, IAuditEntryWriter? audit = null, INotificationWriter? notifications = null) =>
        new QuotinatorWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(initializer);
                services.AddSingleton(audit ?? new NoOpAuditEntryWriter());
                services.AddSingleton<IAuditEntryReader>(new NoOpAuditEntryReader());
                services.AddSingleton<ICallerContext>(new NoOpCallerContext());
                services.AddSingleton(notifications ?? (INotificationWriter)NoOpNotificationWriter.Instance);
                services.AddSingleton<INotificationReader>(new NoOpNotificationReader());
            });
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["Quotinator:AdminApiKey"] = TestKey }));
        });

    private static HttpClient Client(WebApplicationFactory<Program> factory)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", TestKey);
        return client;
    }

    private sealed class RecordingAuditWriter : IAuditEntryWriter
    {
        public List<string> Operations { get; } = [];

        public Task WriteAsync(AuditEntryEntity entry)
        {
            Operations.Add(entry.Operation);
            return Task.CompletedTask;
        }

        public Task WriteAsync(AuditEntryEntity entry, IDbConnection connection, IDbTransaction? transaction = null)
            => WriteAsync(entry);

        public Task WriteAsync(IReadOnlyList<AuditEntryEntity> entries, IDbConnection connection, IDbTransaction? transaction = null)
        {
            foreach (AuditEntryEntity entry in entries)
                Operations.Add(entry.Operation);
            return Task.CompletedTask;
        }

        public Task ClearAsync(string? table = null) => Task.CompletedTask;
    }

    /// <summary>
    /// An initializer whose pending migration is refused for want of a backup until room is freed or the
    /// operator confirms, as the real one is.
    /// </summary>
    private sealed class MigrationSpyInitializer : IDatabaseInitializer
    {
        private static readonly AppliedMigrationResult Pending = new() { Phase = MigrationPhase.App, Version = 99, Description = "The pending fixture migration." };
        private bool _migrated;

        /// <summary>What stops the migration's backup; <see langword="null"/> once there is room.</summary>
        public BackupOutcome? RefuseMigrationWith { get; set; }

        /// <summary>What the last initialisation was told about the confirmation.</summary>
        public bool? LastAllowNoBackup { get; private set; }

        /// <summary>How many times initialisation ran.</summary>
        public int InitialiseCalls { get; private set; }

        public int SchemaVersion => _migrated ? 99 : 98;
        public int DataSchemaVersion => 2;
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
        public bool DatabaseWasCreated => false;
        public IReadOnlyList<AppliedMigrationResult> MigrationsApplied => _migrated ? [Pending] : [];
        public bool SchemaVersionOvershootDetected => false;
        public string DatabaseName => "main";
        public Quotinator.Data.Schema.SchemaIntegrityResult? LastIntegrityResult => null;
        public IReadOnlyList<FileImportReport> LastSeedReport => [];

        public Task<DatabaseOperationResult> InitialiseAsync(bool allowNoBackup = false)
        {
            InitialiseCalls++;
            LastAllowNoBackup = allowNoBackup;
            if (RefuseMigrationWith is BackupOutcome obstacle && !allowNoBackup)
                return Task.FromResult(DatabaseOperationResult.RefusedForBackup(obstacle, BackupGuardedStep.Migration));

            _migrated = true;
            return Task.FromResult(DatabaseOperationResult.Success(
                restorePoint: RefuseMigrationWith is BackupOutcome skippedFor ? RestorePointResult.Skipped(skippedFor) : null));
        }

        public Task<Quotinator.Data.Schema.SchemaIntegrityResult> CheckIntegrityAsync() => Task.FromResult(Quotinator.Data.Schema.SchemaIntegrityResult.Intact);
        public BackupOutcome CheckBackupReadiness(long bytesFreedFirst = 0) => RefuseMigrationWith ?? BackupOutcome.Succeeded;
        public Task<DatabaseBackupResult> CreateBackupAsync() => Task.FromResult(DatabaseBackupResult.Success("backup.db"));
        public Task ReseedAsync(bool forceSourceRefresh = false) => Task.CompletedTask;
        public Task<DatabaseOperationResult> ResetAsync(bool preserveSchemaVersion = false, bool forceSourceRefresh = false, bool allowNoBackup = false) =>
            Task.FromResult(DatabaseOperationResult.Success());
        public Task<SeedPreviewResult> PreviewSeedAsync() => Task.FromResult(new SeedPreviewResult([], []));
        public Task<SourceCacheResolution> RefreshSourcesAsync(bool force = false) => Task.FromResult(new SourceCacheResolution([], []));
    }

    public TestContext TestContext { get; set; } = null!;
}

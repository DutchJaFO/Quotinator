using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// Regression guard for a real bug found live while implementing #279's Step 9: the app's
/// #279-operation-id-rename notification is seeded via the *real* <c>INotificationReader</c>/
/// <c>INotificationWriter</c> (not overridden here, deliberately: most endpoint test files don't
/// override them either), while <see cref="NoOpDatabaseInitializer"/> never creates
/// <c>System_Notification</c>. The first wiring shared its <c>try</c>/<c>catch</c> with
/// <c>Program.cs</c>'s critical DB-init block, so the resulting "no such table" exception marked the
/// whole app unhealthy: 336 of 663 <c>Quotinator.Api.Tests</c> immediately failed. This test proves
/// the fix directly: a failure to seed the announcement notification must never affect
/// <see cref="Quotinator.Api.Startup.DatabaseHealthState"/>.
/// </summary>
[TestClass]
public class ProgramNotificationSeedingRegressionTests
{
    [TestMethod]
    public async Task Health_NoOpDatabaseInitializer_StaysHealthyDespiteMissingNotificationTable()
    {
        using WebApplicationFactory<Program> factory = new QuotinatorWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(NoOpDatabaseInitializer.Instance);
                // INotificationReader/INotificationWriter deliberately NOT overridden; this test
                // exists specifically to prove the real implementations' failure against a
                // non-existent System_Notification table doesn't propagate.
            }));

        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/v1/health", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("{\"status\":\"healthy\"}", await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
    }

    /// <summary>
    /// #289: the second concrete producer for #278's notification mechanism, proving the actual
    /// Program.cs wiring, not just <c>NotificationSeeding.SeedOnceAsync</c> in isolation (covered by
    /// <c>Quotinator.Data.Tests.Notifications.NotificationSeedingTests</c>, where #312 moved both the
    /// helper and its tests). A stub
    /// <see cref="IDatabaseInitializer"/> reports a schema-version overshoot; a real startup must seed
    /// exactly one ActionRequired notification mentioning both recorded versions.
    /// </summary>
    [TestMethod]
    public async Task Startup_SchemaVersionOvershootDetected_SeedsActionRequiredNotification()
    {
        FakeNotificationWriter writer = new();

        using WebApplicationFactory<Program> factory = new QuotinatorWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(new OvershootDatabaseInitializer());
                services.AddSingleton<Quotinator.Data.Repositories.INotificationWriter>(writer);
                services.AddSingleton<Quotinator.Data.Repositories.INotificationReader>(new FakeNotificationReader());
            }));

        using HttpClient client = factory.CreateClient();
        await client.GetAsync("/api/v1/health", TestContext.CancellationToken);

        // #279's own unconditional operation-id-rename notification also seeds whenever dbHealth is
        // healthy, alongside this one; assert on the specific #289 message, not the total count.
        string? overshootMessage = writer.WrittenMessages.SingleOrDefault(m => m.Contains("data v3") && m.Contains("app v5"));
        Assert.IsNotNull(overshootMessage, "the schema-version-overshoot notification must have been seeded");
    }

    /// <summary>
    /// #371: a startup that created the database writes the creation notification, carrying both
    /// schema versions in its payload rather than only in its prose.
    /// </summary>
    [TestMethod]
    public async Task Startup_DatabaseCreated_SeedsTheCreationNotification()
    {
        FakeNotificationWriter writer = new();

        using WebApplicationFactory<Program> factory = Build(writer, new ReportingDatabaseInitializer(databaseWasCreated: true));
        using HttpClient client = factory.CreateClient();
        await client.GetAsync("/api/v1/health", TestContext.CancellationToken);

        // Per the suite's own rule: assert the notification a known cause produces, never a total.
        string? created = writer.WrittenMetadata
            .Where(m => m.Kind == NotificationMetadataKind.DatabaseCreated)
            .Select(m => m.Metadata)
            .SingleOrDefault();

        Assert.IsNotNull(created, "a created database must write exactly one DatabaseCreated notification");
        Assert.Contains("\"dataSchemaVersion\":3", created);
        Assert.Contains("\"appSchemaVersion\":5", created);
    }

    /// <summary>
    /// #371: every migration this startup applied writes its own notification, naming what it did.
    /// </summary>
    [TestMethod]
    public async Task Startup_AfterPendingMigrations_SeedsOneNotificationPerMigration()
    {
        FakeNotificationWriter writer = new();
        AppliedMigrationResult[] applied =
        [
            new AppliedMigrationResult(MigrationPhase.Data, 28, "Widens a probe constraint."),
            new AppliedMigrationResult(MigrationPhase.App, 10, "Adds a probe table."),
        ];

        using WebApplicationFactory<Program> factory = Build(writer, new ReportingDatabaseInitializer(migrationsApplied: applied));
        using HttpClient client = factory.CreateClient();
        await client.GetAsync("/api/v1/health", TestContext.CancellationToken);

        List<string?> migrationPayloads =
            [.. writer.WrittenMetadata.Where(m => m.Kind == NotificationMetadataKind.MigrationApplied).Select(m => m.Metadata)];

        Assert.HasCount(2, migrationPayloads, "two migrations applied, so two notifications");
        Assert.Contains((string? p) => p!.Contains("Widens a probe constraint.", StringComparison.Ordinal), migrationPayloads,
            "a notification must name what the migration did, not only its version");
        Assert.Contains((string? p) => p!.Contains("Adds a probe table.", StringComparison.Ordinal), migrationPayloads,
            "the second migration's own description must appear too");
    }

    /// <summary>
    /// #371 requirement 5, the negative: an ordinary restart against an up-to-date database created
    /// nothing and migrated nothing, so it writes neither notification.
    /// </summary>
    [TestMethod]
    public async Task Startup_WithNothingToReport_SeedsNeitherNotification()
    {
        FakeNotificationWriter writer = new();

        using WebApplicationFactory<Program> factory = Build(writer, new ReportingDatabaseInitializer());
        using HttpClient client = factory.CreateClient();
        await client.GetAsync("/api/v1/health", TestContext.CancellationToken);

        // The positive control for this negative: #279's own announcement still seeds on the same
        // startup, so the writer was reached and an empty result here is a decision, not a dead wire.
        Assert.IsNotEmpty(writer.WrittenMetadata, "the startup producers must have run at all");
        Assert.IsEmpty(
            writer.WrittenMetadata.Where(m => m.Kind is NotificationMetadataKind.DatabaseCreated
                                                     or NotificationMetadataKind.MigrationApplied),
            "neither event happened, so neither notification may be written");
    }

    private static WebApplicationFactory<Program> Build(FakeNotificationWriter writer, IDatabaseInitializer initializer) =>
        new QuotinatorWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton(initializer);
                services.AddSingleton<INotificationWriter>(writer);
                services.AddSingleton<INotificationReader>(new FakeNotificationReader());
            }));

    /// <summary>
    /// #371: reports whatever this startup is meant to have done, so the Program.cs producers can be
    /// driven without a real database. The two version numbers match
    /// <see cref="OvershootDatabaseInitializer"/>'s so both tests read the same pair.
    /// </summary>
    private sealed class ReportingDatabaseInitializer(
        bool databaseWasCreated = false,
        IReadOnlyList<AppliedMigrationResult>? migrationsApplied = null) : IDatabaseInitializer
    {
        public int SchemaVersion => 5;
        public int DataSchemaVersion => 3;
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
        public bool DatabaseWasCreated => databaseWasCreated;
        public IReadOnlyList<AppliedMigrationResult> MigrationsApplied => migrationsApplied ?? [];
        public bool SchemaVersionOvershootDetected => false;
        public IReadOnlyList<FileImportReport> LastSeedReport => [];
        public Task<DatabaseOperationResult> InitialiseAsync() => Task.FromResult(DatabaseOperationResult.Success());

        public BackupOutcome CheckBackupReadiness(long bytesFreedFirst = 0) => BackupOutcome.Succeeded;
        public Task<DatabaseBackupResult> CreateBackupAsync() => Task.FromResult(DatabaseBackupResult.Success("stub-backup.db"));
        public Task ReseedAsync(bool forceSourceRefresh = false) => Task.CompletedTask;
        public Task<DatabaseOperationResult> ResetAsync(bool preserveSchemaVersion = false, bool forceSourceRefresh = false, bool allowNoBackup = false) => Task.FromResult(DatabaseOperationResult.Success());
        public Task<SeedPreviewResult> PreviewSeedAsync() => Task.FromResult(new SeedPreviewResult([], []));
        public Task<SourceCacheResolution> RefreshSourcesAsync(bool force = false) => Task.FromResult(new SourceCacheResolution([], []));
    }

    /// <summary>
    /// #293: same incident class as the two tests above: a live HA v1.8.2 → v1.8.3-beta migration
    /// failure left the database mid-degraded, and <c>DatabaseStatsSummary</c>
    /// (rendered on both Home's degraded modal and the always-reachable <c>/stats</c> page) crashed
    /// the whole page trying to query <c>Import_FileResource</c>/<c>Import_Batch</c>, tables the
    /// failed migration never created. Throwing fakes prove the fix actually skips those calls while
    /// degraded, rather than merely tolerating whatever exception they happen to throw.
    /// </summary>
    [TestMethod]
    public async Task Stats_DatabaseDegraded_RendersWithoutQueryingFileResourceOrImportBatchRepositories()
    {
        using WebApplicationFactory<Program> factory = new QuotinatorWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(new ThrowingInitializeDatabaseInitializer());
                services.AddSingleton<Quotinator.Data.Repositories.INotificationWriter>(new FakeNotificationWriter());
                services.AddSingleton<Quotinator.Data.Repositories.INotificationReader>(new FakeNotificationReader());
                services.AddSingleton<IFileResourceRepository>(new ThrowingFileResourceRepository());
                services.AddSingleton<IImportBatchRepository>(new ThrowingImportBatchRepository());
            }));

        using HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/stats", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "the /stats page must render successfully while degraded, not crash from a live query against not-yet-created tables");
    }

    private sealed class ThrowingInitializeDatabaseInitializer : IDatabaseInitializer
    {
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
        public bool DatabaseWasCreated => false;
        public IReadOnlyList<AppliedMigrationResult> MigrationsApplied => [];
        public bool SchemaVersionOvershootDetected => false;
        public IReadOnlyList<FileImportReport> LastSeedReport => [];
        public Task<DatabaseOperationResult> InitialiseAsync() => throw new InvalidOperationException("simulated migration failure");

        public BackupOutcome CheckBackupReadiness(long bytesFreedFirst = 0) => BackupOutcome.Succeeded;
        public Task<DatabaseBackupResult> CreateBackupAsync() => Task.FromResult(DatabaseBackupResult.Success("stub-backup.db"));
        public Task ReseedAsync(bool forceSourceRefresh = false) => Task.CompletedTask;
        public Task<DatabaseOperationResult> ResetAsync(bool preserveSchemaVersion = false, bool forceSourceRefresh = false, bool allowNoBackup = false) => Task.FromResult(DatabaseOperationResult.Success());
        public Task<SeedPreviewResult> PreviewSeedAsync() => Task.FromResult(new SeedPreviewResult([], []));
        public Task<SourceCacheResolution> RefreshSourcesAsync(bool force = false) => Task.FromResult(new SourceCacheResolution([], []));
    }

    private sealed class ThrowingFileResourceRepository : IFileResourceRepository
    {
        public Task<Guid> WriteAsync(
            string fileName, string? originalFolderPath, FileResourceOrigin origin, string content,
            Guid importBatchId, string? converter = null, string? converterOptions = null,
            string? homeDirectoryKey = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("must not be called while the database is degraded");
        public Task<FileResourceEntity?> FindAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("must not be called while the database is degraded");
        public Task<IReadOnlyList<FileResourceLineEntity>> GetLinesAsync(Guid fileResourceId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("must not be called while the database is degraded");
        public Task<PagedItems<FileResourceListItem>> GetPageAsync(
            string? fileName, FileResourceOrigin? origin, int page, int pageSize, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("must not be called while the database is degraded");
        public Task<IReadOnlyList<Guid>> GetBatchIdsAsync(Guid fileResourceId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("must not be called while the database is degraded");
        public Task<int> PruneAsync(int keepPerFile, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("must not be called while the database is degraded");
    }

    private sealed class ThrowingImportBatchRepository : IImportBatchRepository
    {
        private static InvalidOperationException NotExpected() => new("must not be called while the database is degraded");
        public Task<ImportBatchEntity?> GetByIdAsync(Guid id, IUnitOfWork? unitOfWork = null) => throw NotExpected();
        public Task<IReadOnlyList<ImportBatchEntity>> GetAllAsync(IUnitOfWork? unitOfWork = null) => throw NotExpected();
        public Task<IReadOnlyList<ImportBatchEntity>> GetByTypeAsync(ImportBatchType type, IUnitOfWork? unitOfWork = null) => throw NotExpected();
        public Task<PagedItems<ImportBatchEntity>> GetPagedAsync(ImportBatchType? type, ImportBatchStatus? status, int page, int pageSize) => throw NotExpected();
        public Task UpdateRecordCountAsync(Guid id, int count, IUnitOfWork? unitOfWork = null) => throw NotExpected();
        public Task InsertAsync(ImportBatchEntity entity, IUnitOfWork? unitOfWork = null) => throw NotExpected();
        public Task InsertManyAsync(IEnumerable<ImportBatchEntity> entities, IUnitOfWork? unitOfWork = null, InsertStrategy strategy = InsertStrategy.Bulk) => throw NotExpected();
        public Task UpdateAsync(ImportBatchEntity entity, IUnitOfWork? unitOfWork = null) => throw NotExpected();
        public Task SoftDeleteAsync(Guid id, IUnitOfWork? unitOfWork = null) => throw NotExpected();
    }

    private sealed class OvershootDatabaseInitializer : IDatabaseInitializer
    {
        public int SchemaVersion => 5;
        public int DataSchemaVersion => 3;
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
        public IReadOnlyList<AppliedMigrationResult> MigrationsApplied => [];
        public bool SchemaVersionOvershootDetected => true;
        public IReadOnlyList<FileImportReport> LastSeedReport => [];
        public Task<DatabaseOperationResult> InitialiseAsync() => Task.FromResult(DatabaseOperationResult.Success());

        public BackupOutcome CheckBackupReadiness(long bytesFreedFirst = 0) => BackupOutcome.Succeeded;
        public Task<DatabaseBackupResult> CreateBackupAsync() => Task.FromResult(DatabaseBackupResult.Success("stub-backup.db"));
        public Task ReseedAsync(bool forceSourceRefresh = false) => Task.CompletedTask;
        public Task<DatabaseOperationResult> ResetAsync(bool preserveSchemaVersion = false, bool forceSourceRefresh = false, bool allowNoBackup = false) => Task.FromResult(DatabaseOperationResult.Success());
        public Task<SeedPreviewResult> PreviewSeedAsync() => Task.FromResult(new SeedPreviewResult([], []));
        public Task<SourceCacheResolution> RefreshSourcesAsync(bool force = false) => Task.FromResult(new SourceCacheResolution([], []));
    }

    public TestContext TestContext { get; set; }
}

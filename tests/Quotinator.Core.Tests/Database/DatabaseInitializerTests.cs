using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Core.Enums;
using Quotinator.Core.Models;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Import;
using Quotinator.Data.Notifications;
using Quotinator.Data.Paths;
using Quotinator.Data.Queries;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.Database;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Core.Database;
using Quotinator.Core.Entities;
using Quotinator.Core.Services;

namespace Quotinator.Core.Tests.Database;

[TestClass]
public class DatabaseInitializerTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static readonly string SourcesDir = Path.Combine(RepoRoot, "data", "sources");

    private static string CuratedFile       => Path.Combine(SourcesDir, "quotinator-curated.json");
    private static string VilaboimFile      => Path.Combine(SourcesDir, "vilaboim_movie-quotes.json");
    private static string NikhilNamal17File => Path.Combine(SourcesDir, "NikhilNamal17_popular-movie-quotes.json");

    private string _tempDir = null!;
    private string _dbPath  = null!;
    private string _backups = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");
        _backups = Path.Combine(_tempDir, "backups");
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private QuotinatorDatabaseInitializer CreateInitializer(
        IReadOnlyList<SeedBatch> batches, bool useBaseline = true,
        IRuleFileOverridePathResolver? ruleFileOverridePathResolver = null, ISourceFileOverrideRegistry? sourceFileOverrideRegistry = null,
        bool autoPurgeBundledImportActions = false, bool autoPurgeUserImportActions = false,
        IAuditEntryWriter? auditWriter = null,
        IDiskSpaceProvider? diskSpaceProvider = null, int? maxBackupStorageGb = null,
        ISourceCacheUpdater? sourceCacheUpdater = null, bool autoUpdateSources = false,
        IAppVersionTracker? appVersionTracker = null)
        => CreateInitializer(batches, QuotinatorMigrations.All, useBaseline, ruleFileOverridePathResolver, sourceFileOverrideRegistry,
            autoPurgeBundledImportActions, autoPurgeUserImportActions, auditWriter, diskSpaceProvider, maxBackupStorageGb,
            sourceCacheUpdater, autoUpdateSources, appVersionTracker);

    private QuotinatorDatabaseInitializer CreateInitializer(
        IReadOnlyList<SeedBatch> batches, IReadOnlyList<SchemaMigration> migrations, bool useBaseline,
        IRuleFileOverridePathResolver? ruleFileOverridePathResolver = null, ISourceFileOverrideRegistry? sourceFileOverrideRegistry = null,
        bool autoPurgeBundledImportActions = false, bool autoPurgeUserImportActions = false,
        IAuditEntryWriter? auditWriter = null,
        IDiskSpaceProvider? diskSpaceProvider = null, int? maxBackupStorageGb = null,
        ISourceCacheUpdater? sourceCacheUpdater = null, bool autoUpdateSources = false,
        IAppVersionTracker? appVersionTracker = null)
    {
        SqliteConnectionFactory factory       = new SqliteConnectionFactory(_dbPath);
        DatabaseOptions options       = new DatabaseOptions { DbPath = _dbPath, BackupsPath = _backups, MaxBackupStorageGb = maxBackupStorageGb ?? 1 };
        SqliteImportBatchRepository importBatches = new SqliteImportBatchRepository(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance);
        NullLogger<DatabaseInitializer> logger        = NullLogger<DatabaseInitializer>.Instance;
        ImportActionReader actionReader   = new ImportActionReader(factory);
        ImportActionWriter actionWriter   = new ImportActionWriter(factory);
        ImportActionResolutionCoordinator coordinator    = new ImportActionResolutionCoordinator(actionReader, actionWriter, factory);
        SqliteImportActionService actionService  = new SqliteImportActionService(actionReader, coordinator, actionWriter, NoOpAuditEntryWriter.Instance, NoOpChangeWriter.Instance,
            new SqliteRestorableRepository<QuoteEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<SourceEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<CharacterEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<PersonEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<ConversationEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<StageDirectionEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<SoundCueEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            importBatches, factory, NoOpNotificationWriter.Instance);
        return new QuotinatorDatabaseInitializer(factory, options, migrations, batches, importBatches,
            coordinator, actionService, actionWriter,
            auditWriter ?? NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance, logger,
            sourceCacheUpdater ?? NoOpSourceCacheUpdater.Instance, autoUpdateSources,
            autoPurgeBundledImportActions, autoPurgeUserImportActions,
            ruleFileOverridePathResolver ?? NoOpRuleFileOverridePathResolver.Instance,
            sourceFileOverrideRegistry ?? NoOpSourceFileOverrideRegistry.Instance,
            NoOpFileResourceRepository.Instance,
            TestNotificationReader.Create(factory),
            new NotificationWriter(factory),
            NoOpNotificationTextSource.Instance,
            appVersionTracker ?? new AppVersionTracker(factory),
            new VersionService(),
            diskSpaceProvider ?? NoOpDiskSpaceProvider.Instance,
            useBaseline ? QuotinatorMigrations.Baseline : null);
    }

    /// <summary>
    /// #304 trigger 1: source content changed upstream on a database that already held quotes, so the
    /// seeded data no longer reflects the sources and a reseed is worth recommending.
    /// </summary>
    [TestMethod]
    public async Task Initialise_ContentChangedOnNonEmptyDatabase_WritesReseedRecommendation()
    {
        await SeedThenReinitialiseAsync(SourceRefreshOutcome.Updated, autoUpdateSources: true);

        NotificationEntity notification = (await NotificationsAsync()).Single();

        Assert.AreEqual(NotificationType.ActionRequired, notification.Type.Parsed);
        Assert.AreEqual(NotificationDismissTrigger.Reseed, notification.DismissTriggerKey.Parsed);
        Assert.AreEqual(NotificationMetadataKind.ReseedRecommended, notification.MetadataKind.Parsed);
    }

    /// <summary>Nothing changed upstream, so there is nothing to recommend.</summary>
    [TestMethod]
    public async Task Initialise_NoSourceContentChanged_WritesNoNotification()
    {
        await SeedThenReinitialiseAsync(SourceRefreshOutcome.UpToDate, autoUpdateSources: true);

        Assert.IsEmpty(await NotificationsAsync());
    }

    /// <summary>
    /// The database was empty, so the seed itself applied the new content — recommending a reseed of
    /// content that just landed would be noise.
    /// </summary>
    [TestMethod]
    public async Task Initialise_EmptyDatabaseSeeded_WritesNoNotification()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer(
            [AllFilesBatch()], sourceCacheUpdater: new StubSourceCacheUpdater(SourceRefreshOutcome.Updated),
            autoUpdateSources: true);
        await db.InitialiseAsync();

        Assert.IsEmpty(await NotificationsAsync(),
            "The seed applied the changed content on this very run — there is nothing left to recommend.");
    }

    /// <summary>
    /// With auto-update off no network check runs at all, so there is no basis on which to claim
    /// anything changed.
    /// </summary>
    [TestMethod]
    public async Task Initialise_AutoUpdateSourcesDisabled_WritesNoNotification()
    {
        await SeedThenReinitialiseAsync(SourceRefreshOutcome.Updated, autoUpdateSources: false);

        Assert.IsEmpty(await NotificationsAsync());
    }

    /// <summary>
    /// Seeds a database, then initialises a second time against the same file with the given refresh
    /// outcome — the "already had content, then sources changed" shape trigger 1 fires on.
    /// </summary>
    private async Task SeedThenReinitialiseAsync(SourceRefreshOutcome outcome, bool autoUpdateSources)
    {
        QuotinatorDatabaseInitializer seeded = CreateInitializer([AllFilesBatch()]);
        await seeded.InitialiseAsync();

        QuotinatorDatabaseInitializer again = CreateInitializer(
            [AllFilesBatch()], sourceCacheUpdater: new StubSourceCacheUpdater(outcome),
            autoUpdateSources: autoUpdateSources);
        await again.InitialiseAsync();
    }

    private async Task<IReadOnlyList<NotificationEntity>> NotificationsAsync()
        => (await TestNotificationReader.Create(_dbPath).GetPagedAsync(1, 0)).Items;

    /// <summary>
    /// Reports a fixed outcome for every candidate file, without touching the network — the refresh
    /// result is trigger 1's input, and the point here is what the initializer does with it.
    /// </summary>
    private sealed class StubSourceCacheUpdater(SourceRefreshOutcome outcome) : ISourceCacheUpdater
    {
        public Task<SourceCacheResolution> ResolveAsync(
            IReadOnlyList<SeedBatch> candidateBatches, bool allowNetwork, bool forceRefresh,
            CancellationToken cancellationToken = default)
        {
            List<SourceRefreshResult> results = [.. candidateBatches
                .SelectMany(b => b.Files)
                .Select(f => new SourceRefreshResult(Path.GetFileName(f.FilePath), "https://example.invalid", outcome))];

            return Task.FromResult(new SourceCacheResolution(candidateBatches, results));
        }
    }

    private static SeedBatch AllFilesBatch() => new(
        [
            new SeedFile(CuratedFile,        null),
            new SeedFile(VilaboimFile,        "https://github.com/vilaboim/movie-quotes"),
            new SeedFile(NikhilNamal17File,   "https://github.com/NikhilNamal17/popular-movie-quotes")
        ],
        ManifestPolicy.HardcodedDefault,
        "bundled sources");

    /// <summary>#153: mirrors production's manifest-driven wiring — NikhilNamal17 seeded under its own
    /// Review policy with its real ruleFile, matching what ManifestSeedPlanner actually builds (unlike
    /// <see cref="AllFilesBatch"/>, which never wires a rule file at all).</summary>
    private static SeedBatch NikhilNamal17WithRuleFileBatch() => new(
        [
            new SeedFile(
                NikhilNamal17File,
                "https://github.com/NikhilNamal17/popular-movie-quotes",
                Policy: new ManifestPolicy(DuplicateResolutionPolicy.Review),
                RuleFilePath: Path.Combine(SourcesDir, "nikhilnamal17-conflict-rules.json"),
                SourceAliasFilePath: Path.Combine(SourcesDir, "nikhilnamal17-source-aliases.json"))
        ],
        ManifestPolicy.HardcodedDefault,
        "bundled sources");

    /// <summary>#153: seeding NikhilNamal17 alone, under its real Review policy and real rule file, must
    /// produce zero Pending/Stale/Blocked actions — the same "no file left staged awaiting review"
    /// invariant CLAUDE.md's own live T2 checklist already asserts against the full bundled dataset.</summary>
    [TestMethod]
    public async Task InitialiseAsync_NikhilNamal17WithRealRuleFile_ProducesNoUnresolvedActions()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([NikhilNamal17WithRuleFileBatch()]);
        await db.InitialiseAsync();

        IReadOnlyList<ImportActionEntity> allActions = (await new ImportActionReader(new SqliteConnectionFactory(_dbPath)).GetPagedAsync(null, null, null, 1, 0)).Items;
        List<ImportActionEntity> unresolved = [.. allActions.Where(a => a.Status.Parsed is not (ImportActionStatus.Decided or ImportActionStatus.Applied))];

        Assert.IsEmpty(unresolved,
            $"Every action must auto-resolve under Review with the real rule file — found: {string.Join(" | ", unresolved.Select(u => $"{u.EntityId}:{u.Status.Raw} existing={u.ExistingValue} incoming={u.IncomingValue}"))}");
    }

    /// <summary>#153: the Galadriel Custom rule (nikhilnamal17-conflict-rules.json) must correct the
    /// character field on this quote's very first (Add) encounter, not only on a later Modify.</summary>
    [TestMethod]
    public async Task InitialiseAsync_NikhilNamal17WithRealRuleFile_GaladrielQuoteGetsCharacterOnAdd()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([NikhilNamal17WithRuleFileBatch()]);
        await db.InitialiseAsync();

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);

        string? character = await conn.ExecuteScalarAsync<string?>(
            "SELECT c.Name FROM Quotinator_Quote q JOIN Quotinator_Character c ON c.Id = q.CharacterId " +
            "WHERE q.Id = 'c124e692-04fc-7b49-af53-b6bcc0692dbe' AND q.IsDeleted = 0;");

        Assert.AreEqual("Galadriel", character);
    }

    // ── Seeding ───────────────────────────────────────────────────────────────

    /// <summary>Seeding all three bundled source files produces the expected quote/source/character counts.</summary>
    [TestMethod]
    public async Task InitialiseAsync_AllSourceFiles_SeedsExpectedCounts()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        Assert.AreEqual(799, db.QuoteCount,     "Unique quotes");
        Assert.AreEqual(482, db.SourceCount,    "Sources");
        Assert.AreEqual(7,   db.CharacterCount, "Characters");
        Assert.AreEqual(3,   db.PeopleCount,    "People (Winston Churchill, Neil Armstrong, Martin Luther King Jr. — curated)");
    }

    /// <summary>#221: the five entity-type counts added alongside Quote/Source/Character/People
    /// (Series/Universe/StageDirection/SoundCue/Conversation) are each populated from a live query
    /// against their own table, not left at zero — cross-checked directly against SQL rather than a
    /// hardcoded literal, since the exact bundled totals are incidental to this test's purpose.</summary>
    [TestMethod]
    public async Task InitialiseAsync_AllSourceFiles_PopulatesNewEntityTypeCounts()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);

        Assert.AreEqual(await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_Series WHERE IsDeleted = 0;"), db.SeriesCount);
        Assert.AreEqual(await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_Universe WHERE IsDeleted = 0;"), db.UniverseCount);
        Assert.AreEqual(await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_StageDirection WHERE IsDeleted = 0;"), db.StageDirectionCount);
        Assert.AreEqual(await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_SoundCue WHERE IsDeleted = 0;"), db.SoundCueCount);
        Assert.AreEqual(await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_Conversation WHERE IsDeleted = 0;"), db.ConversationCount);
        Assert.AreEqual(0, db.SeriesCount, "AllFilesBatch() (curated/vilaboim/NikhilNamal17) does not include the separate series-universe bundled file");
        Assert.AreEqual(0, db.UniverseCount, "AllFilesBatch() (curated/vilaboim/NikhilNamal17) does not include the separate series-universe bundled file");
        Assert.IsGreaterThan(0, db.StageDirectionCount, "Bundled data includes at least one StageDirection");
        Assert.IsGreaterThan(0, db.SoundCueCount, "Bundled data includes at least one SoundCue");
        Assert.IsGreaterThan(0, db.ConversationCount, "Bundled data includes at least one Conversation");
    }

    /// <summary>#221: cross-file duplicates between vilaboim and NikhilNamal17 show up as "modified" Quote
    /// actions in the per-file report (AllFilesBatch() uses ManifestPolicy.HardcodedDefault, i.e.
    /// NewestWins, bypassing the bundled manifest.json's own "skip" override) — none pending or blocked,
    /// since NewestWins always resolves deterministically.</summary>
    [TestMethod]
    public async Task InitialiseAsync_AllSourceFiles_TracksCrossFileDuplicates()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        int modified = db.LastSeedReport.Sum(r => r.EntityTypes.GetValueOrDefault("Quote")?.Modified ?? 0);
        int pending  = db.LastSeedReport.Sum(r => r.EntityTypes.GetValueOrDefault("Quote")?.Pending ?? 0);
        int blocked  = db.LastSeedReport.Sum(r => r.EntityTypes.GetValueOrDefault("Quote")?.Blocked ?? 0);

        Assert.AreEqual(45, modified, "Cross-file duplicates, resolved as modified Quote actions");
        Assert.AreEqual(0, pending, "NewestWins always resolves deterministically — nothing pending");
        Assert.AreEqual(0, blocked, "NewestWins never blocks — no Complete rows exist yet to block against");
    }

    /// <summary>#221: PreviewSeedAsync must produce the same rich per-file, per-entity-type report as a
    /// real seed run, computed via a read-only <see cref="ImportActionPlanner.PlanAsync"/> call — but
    /// write nothing to the database. Run against an already-fully-seeded database (so the planner has
    /// real rows to resolve against, not just "everything new"), asserting both that the report has the
    /// expected shape and, critically, that System_ImportActions/ImportBatches row counts are completely
    /// unchanged before and after the call.</summary>
    [TestMethod]
    public async Task PreviewSeedAsync_AfterFullSeed_ProducesReportWithoutWritingAnyRow()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);

        ImportActionReader actionReader = new ImportActionReader(new SqliteConnectionFactory(_dbPath));
        int actionsBefore = (await actionReader.GetPagedAsync(null, null, null, 1, 0)).TotalCount;
        int batchesBefore = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Import_Batch;");

        SeedPreviewResult preview = await db.PreviewSeedAsync();

        int actionsAfter = (await actionReader.GetPagedAsync(null, null, null, 1, 0)).TotalCount;
        int batchesAfter = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Import_Batch;");

        Assert.AreEqual(actionsBefore, actionsAfter, "PreviewSeedAsync must never write to Import_Action");
        Assert.AreEqual(batchesBefore, batchesAfter, "PreviewSeedAsync must never create an ImportBatches row");

        Assert.HasCount(preview.Files.Count, preview.Reports, "One report per previewed file");
        Assert.IsTrue(preview.Reports.All(r => r.EntityTypes.ContainsKey("Quote")), "Every bundled file has at least one Quote action");

        int modified = preview.Reports.Sum(r => r.EntityTypes.GetValueOrDefault("Quote")?.Modified ?? 0);
        int newCount = preview.Reports.Sum(r => r.EntityTypes.GetValueOrDefault("Quote")?.New ?? 0);
        Assert.AreEqual(844, modified, "799 unique quotes + 45 cross-file duplicate occurrences (AllFilesBatch's own vilaboim/NikhilNamal17 overlap) — every quote line across every file matches an already-existing row, since the database was already fully seeded");
        Assert.AreEqual(0, newCount, "Nothing is genuinely new — the database was already fully seeded from the same files");
    }

    // ── #249: conflict-resolution data auto-purge ───────────────────────────

    [TestMethod]
    public async Task InitialiseAsync_AutoPurgeBundledTrue_FullyAppliedBundledBatch_PurgesImportActionRows()
    {
        SeedBatch batch = new SeedBatch([new SeedFile(CuratedFile, null)], ManifestPolicy.HardcodedDefault, "curated", SeedBatchOrigin.Bundled);
        QuotinatorDatabaseInitializer db = CreateInitializer([batch], autoPurgeBundledImportActions: true, autoPurgeUserImportActions: false);
        await db.InitialiseAsync();

        ImportActionReader actionReader = new ImportActionReader(new SqliteConnectionFactory(_dbPath));
        int remaining    = (await actionReader.GetPagedAsync(null, null, null, 1, 0)).TotalCount;

        Assert.AreEqual(0, remaining, "a fully-applied bundled batch's Import_Action rows must be purged when AutoPurgeBundledImportActions is true");
    }

    [TestMethod]
    public async Task InitialiseAsync_AutoPurgeBundledFalse_FullyAppliedBundledBatch_RetainsImportActionRows()
    {
        SeedBatch batch = new SeedBatch([new SeedFile(CuratedFile, null)], ManifestPolicy.HardcodedDefault, "curated", SeedBatchOrigin.Bundled);
        QuotinatorDatabaseInitializer db = CreateInitializer([batch], autoPurgeBundledImportActions: false, autoPurgeUserImportActions: true);
        await db.InitialiseAsync();

        ImportActionReader actionReader = new ImportActionReader(new SqliteConnectionFactory(_dbPath));
        int remaining    = (await actionReader.GetPagedAsync(null, null, null, 1, 0)).TotalCount;

        Assert.IsGreaterThan(0, remaining, "a bundled batch's Import_Action rows must be retained when AutoPurgeBundledImportActions is false, regardless of the user-imports setting");
    }

    [TestMethod]
    public async Task InitialiseAsync_AutoPurgeUserImportsTrue_FullyAppliedUserOriginBatch_PurgesImportActionRows()
    {
        SeedBatch batch = new SeedBatch([new SeedFile(CuratedFile, null)], ManifestPolicy.HardcodedDefault, "user", SeedBatchOrigin.UserImports);
        QuotinatorDatabaseInitializer db = CreateInitializer([batch], autoPurgeBundledImportActions: false, autoPurgeUserImportActions: true);
        await db.InitialiseAsync();

        ImportActionReader actionReader = new ImportActionReader(new SqliteConnectionFactory(_dbPath));
        int remaining    = (await actionReader.GetPagedAsync(null, null, null, 1, 0)).TotalCount;

        Assert.AreEqual(0, remaining, "a fully-applied user-imports batch's Import_Action rows must be purged when AutoPurgeUserImportActions is true, independent of the bundled setting");
    }

    [TestMethod]
    public async Task InitialiseAsync_AutoPurgeUserImportsFalse_UserOriginBatch_RetainsImportActionRowsEvenWhenBundledTrue()
    {
        SeedBatch batch = new SeedBatch([new SeedFile(CuratedFile, null)], ManifestPolicy.HardcodedDefault, "user", SeedBatchOrigin.UserImports);
        QuotinatorDatabaseInitializer db = CreateInitializer([batch], autoPurgeBundledImportActions: true, autoPurgeUserImportActions: false);
        await db.InitialiseAsync();

        ImportActionReader actionReader = new ImportActionReader(new SqliteConnectionFactory(_dbPath));
        int remaining    = (await actionReader.GetPagedAsync(null, null, null, 1, 0)).TotalCount;

        Assert.IsGreaterThan(0, remaining, "a user-imports batch must not be purged by the bundled setting — the two per-origin settings are independent");
    }

    [TestMethod]
    public async Task InitialiseAsync_AutoPurgeEnabled_WritesAuditEntryRecordingThePurge()
    {
        SeedBatch batch            = new SeedBatch([new SeedFile(CuratedFile, null)], ManifestPolicy.HardcodedDefault, "curated", SeedBatchOrigin.Bundled);
        List<AuditEntryEntity> capturedEntries  = [];
        CapturingAuditEntryWriter capturingWriter  = new CapturingAuditEntryWriter(capturedEntries);
        QuotinatorDatabaseInitializer db = CreateInitializer([batch], autoPurgeBundledImportActions: true, autoPurgeUserImportActions: false, auditWriter: capturingWriter);
        await db.InitialiseAsync();

        Assert.Contains(e => e.TableName == "Import_Action" && e.Operation == AuditOperation.Purge, capturedEntries,
            "a purge must leave a permanent trace in the audit trail, even though the underlying resolution data itself is gone");
    }

    private sealed class CapturingAuditEntryWriter(List<AuditEntryEntity> entries) : IAuditEntryWriter
    {
        public Task WriteAsync(AuditEntryEntity entry, System.Data.IDbConnection connection, System.Data.IDbTransaction? transaction = null)
        {
            entries.Add(entry);
            return Task.CompletedTask;
        }
        public Task WriteAsync(IReadOnlyList<AuditEntryEntity> entries2, System.Data.IDbConnection connection, System.Data.IDbTransaction? transaction = null)
        {
            entries.AddRange(entries2);
            return Task.CompletedTask;
        }
        public Task WriteAsync(AuditEntryEntity entry)
        {
            entries.Add(entry);
            return Task.CompletedTask;
        }
        public Task ClearAsync(string? table = null) => Task.CompletedTask;
    }

    /// <summary>Seeding only the curated file correctly wires up the FK chain: Source → Character → Quote.</summary>
    [TestMethod]
    public async Task InitialiseAsync_CuratedFileOnly_SeedsFkChainCorrectly()
    {
        SeedBatch batch = new SeedBatch([new SeedFile(CuratedFile, null)], ManifestPolicy.HardcodedDefault, "curated");
        QuotinatorDatabaseInitializer db = CreateInitializer([batch]);
        await db.InitialiseAsync();

        Assert.AreEqual(13, db.QuoteCount,     "10 movie/tv quotes (Airplane!, Holy Grail, Princess Bride, Star Wars) + 3 person quotes (Churchill, Armstrong, MLK)");
        Assert.AreEqual(7,  db.SourceCount,    "4 movie sources + 3 person speech occasions");
        Assert.AreEqual(7,  db.CharacterCount, "7 characters across the four movie sources");
        Assert.AreEqual(3,  db.PeopleCount,    "Winston Churchill, Neil Armstrong, Martin Luther King Jr.");
        Assert.AreEqual(0,  db.LastSeedReport.Sum(r => r.EntityTypes.GetValueOrDefault("Quote")?.Modified ?? 0), "A single file has no cross-file duplicates");
    }

    /// <summary>
    /// The curated file's explicit <c>people[]</c> entries (Winston Churchill, Neil Armstrong, Martin
    /// Luther King Jr.) carry real dateOfBirth/dateOfDeath — added specifically to exercise the Person
    /// Add write path with real data, after a live T2 pass found it silently dropped both fields on a
    /// brand-new PersonEntity (see <c>SqliteImportActionServiceTests.ApplyBatchAsync_PersonAdd_WritesDateOfBirthAndDateOfDeath</c>
    /// for the isolated regression test; this is the end-to-end seeding equivalent).
    /// </summary>
    [TestMethod]
    public async Task InitialiseAsync_CuratedFileOnly_SeedsPersonDatesFromExplicitEntries()
    {
        SeedBatch batch = new SeedBatch([new SeedFile(CuratedFile, null)], ManifestPolicy.HardcodedDefault, "curated");
        QuotinatorDatabaseInitializer db = CreateInitializer([batch]);
        await db.InitialiseAsync();

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);

        List<(string Name, string? DateOfBirth, string? DateOfDeath)> people = [.. (await conn.QueryAsync<(string Name, string? DateOfBirth, string? DateOfDeath)>(
            "SELECT Name, DateOfBirth, DateOfDeath FROM Quotinator_Person WHERE IsDeleted = 0;"))];

        Assert.HasCount(3, people);
        Assert.Contains(p => p is { Name: "Winston Churchill", DateOfBirth: "1874-11-30", DateOfDeath: "1965-01-24" }, people);
        Assert.Contains(p => p is { Name: "Neil Armstrong", DateOfBirth: "1930-08-05", DateOfDeath: "2012-08-25" }, people);
        Assert.Contains(p => p is { Name: "Martin Luther King Jr.", DateOfBirth: "1929-01-15", DateOfDeath: "1968-04-04" }, people);
    }

    /// <summary>#191: a Source discovered implicitly from a quote (never named in a sources[] section) still carries that quote's own Date once seeded — the curated file's own Airplane!/1980 entries are the fixture.</summary>
    [TestMethod]
    public async Task InitialiseAsync_AllSourceFiles_SeedsSourceDatesFromQuotes()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);

        string? airplaneDate = await conn.ExecuteScalarAsync<string?>(
            "SELECT Date FROM Quotinator_Source WHERE Title = 'Airplane!' AND Type = 'Movie' AND IsDeleted = 0;");
        Assert.AreEqual("1980", airplaneDate, "Sources.Date must be populated from the resolving quote's own Date");

        int datedSourceCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Quotinator_Source WHERE Date IS NOT NULL AND IsDeleted = 0;");
        Assert.IsGreaterThan(0, datedSourceCount, "At least some seeded Sources must now carry a Date — today every one of them is null");
    }

    /// <summary>#245: a Source first created date-less via a sources[] entry (e.g. #180's Series-linking-only shape) must have its Date backfilled once a later-seeded file's quote supplies one — #191 only ever fixed the never-named-in-a-file case, not this one.</summary>
    [TestMethod]
    public async Task InitialiseAsync_DatelessSourcesEntryThenDatedQuoteInLaterFile_BackfillsSourceDate()
    {
        string datelessEntryFile = Path.Combine(_tempDir, "dateless-entry.json");
        string datedQuoteFile    = Path.Combine(_tempDir, "dated-quote.json");

        File.WriteAllText(datelessEntryFile,
            """{"quotes":[],"sources":[{"title":"Test Film","type":"movie"}]}""");
        File.WriteAllText(datedQuoteFile,
            """{"quotes":[{"id":"e1111111-1111-4111-8111-111111111111","quote":"A test line.","originalLanguage":"en","source":"Test Film","date":"1999","character":null,"author":null,"type":"movie","genres":[],"translations":{}}],"sources":[]}""");

        SeedBatch batch = new SeedBatch(
            [
                new SeedFile(datelessEntryFile, null),
                new SeedFile(datedQuoteFile, null),
            ],
            ManifestPolicy.HardcodedDefault, "dateless-entry-test");

        QuotinatorDatabaseInitializer db = CreateInitializer([batch]);
        await db.InitialiseAsync();

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);

        string? date = await conn.ExecuteScalarAsync<string?>(
            "SELECT Date FROM Quotinator_Source WHERE Title = 'Test Film' AND Type = 'Movie' AND IsDeleted = 0;");
        Assert.AreEqual("1999", date, "The dateless sources[] entry's Source must be backfilled from the later file's own dated quote");
    }

    /// <summary>
    /// #68: seeding the curated file writes its four conversations (Airplane!, Holy Grail, Princess
    /// Bride, Empire Strikes Back) into Conversations/ConversationLines/StageDirections/SoundCues,
    /// all sharing the file's own ImportBatchId, staged through System_ImportActions like every
    /// other entity type.
    /// </summary>
    [TestMethod]
    public async Task InitialiseAsync_CuratedFileOnly_SeedsConversationsStageDirectionsAndSoundCues()
    {
        SeedBatch batch = new SeedBatch([new SeedFile(CuratedFile, null)], ManifestPolicy.HardcodedDefault, "curated");
        QuotinatorDatabaseInitializer db = CreateInitializer([batch]);
        await db.InitialiseAsync();

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);

        int conversationCount    = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_Conversation WHERE IsDeleted = 0;");
        int stageDirectionCount  = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_StageDirection WHERE IsDeleted = 0;");
        int soundCueCount        = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_SoundCue WHERE IsDeleted = 0;");
        int conversationLineCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_ConversationLine WHERE IsDeleted = 0;");

        Assert.AreEqual(4, conversationCount,     "4 conversations (Airplane!, Holy Grail, Princess Bride, Empire Strikes Back)");
        Assert.AreEqual(2, stageDirectionCount,   "2 stage directions (Princess Bride, Empire Strikes Back)");
        Assert.AreEqual(1, soundCueCount,         "1 sound cue (Holy Grail)");
        Assert.AreEqual(13, conversationLineCount, "2 + 4 + 2 + 5 lines across the four conversations");

        IEnumerable<string> distinctBatchIds = await conn.QueryAsync<string>(
            "SELECT DISTINCT ImportBatchId FROM Quotinator_Conversation UNION SELECT DISTINCT ImportBatchId FROM Quotinator_StageDirection UNION SELECT DISTINCT ImportBatchId FROM Quotinator_SoundCue;");
        Assert.HasCount(1, distinctBatchIds.ToList(), "All conversation-related rows from one file should share one ImportBatchId");

        ImportActionReader actionReader = new ImportActionReader(new SqliteConnectionFactory(_dbPath));
        List<string> actionEntityTypes = [];
        foreach (string? entityType in new[] { "Conversation", "StageDirection", "SoundCue" })
        {
            if ((await actionReader.GetPagedAsync(null, null, entityType, 1, 0)).TotalCount > 0)
                actionEntityTypes.Add(entityType);
        }
        Assert.AreSequenceEqual(["Conversation", "StageDirection", "SoundCue"], actionEntityTypes, Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder, "Conversation/StageDirection/SoundCue Add actions must be staged through Import_Action like every other entity type");
    }

    /// <summary>
    /// #68: reseeding (clear + reimport from source) reproduces the same conversation/stage-direction/
    /// sound-cue counts, not doubled — exercises the parse-plan-apply path a second time from a clean
    /// slate. Live re-import-without-clearing dedup (Add-detection by explicit id) is covered
    /// directly against <c>SqliteQuoteImportService</c> in
    /// <c>QuoteImportServiceTests.ImportAsync_SameExtendedFormatFileImportedTwice_DoesNotDuplicateConversationOrStageDirection</c>,
    /// since <see cref="QuotinatorDatabaseInitializer"/>'s own seeding is a no-op once any quote
    /// already exists (see <see cref="InitialiseAsync_CalledTwice_IsIdempotent"/>) and so cannot
    /// exercise that scenario itself.
    /// </summary>
    [TestMethod]
    public async Task ReseedAsync_CuratedFileOnly_ReproducesSameConversationCountsNotDoubled()
    {
        SeedBatch batch = new SeedBatch([new SeedFile(CuratedFile, null)], ManifestPolicy.HardcodedDefault, "curated");
        QuotinatorDatabaseInitializer db = CreateInitializer([batch]);
        await db.InitialiseAsync();
        await db.ReseedAsync();

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);

        Assert.AreEqual(4, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_Conversation WHERE IsDeleted = 0;"));
        Assert.AreEqual(2, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_StageDirection WHERE IsDeleted = 0;"));
        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_SoundCue WHERE IsDeleted = 0;"));
        Assert.AreEqual(13, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_ConversationLine WHERE IsDeleted = 0;"));
    }

    // ── #181: per-source conflict-resolution rule file ──────────────────────────

    /// <summary>
    /// End-to-end seeding proof: a second file re-introducing an already-seeded quote under Review
    /// policy, with a matching per-source rule (Keep) for the only field that differs, auto-resolves
    /// and applies immediately at startup instead of leaving a Pending action stuck in
    /// System_ImportActions — no manual decide/apply step needed.
    /// </summary>
    [TestMethod]
    public async Task InitialiseAsync_SecondFileReviewPolicyMatchingRule_AutoResolvesNoPendingActionLeft()
    {
        const string quoteId = "d1111111-1111-4111-8111-111111111111";
        string baselinePath = Path.Combine(_tempDir, "baseline.json");
        string conflictPath = Path.Combine(_tempDir, "conflict.json");
        string rulesPath    = Path.Combine(_tempDir, "conflict-rules.json");

        File.WriteAllText(baselinePath,
            """[{"id":"QUOTE_ID","quote":"Original text.","originalLanguage":"en","source":"Test Film","date":"2000","character":null,"author":null,"type":"movie","genres":[],"translations":{}}]"""
                .Replace("QUOTE_ID", quoteId));
        File.WriteAllText(conflictPath,
            """[{"id":"QUOTE_ID","quote":"Changed text.","originalLanguage":"en","source":"Test Film","date":"2000","character":null,"author":null,"type":"movie","genres":[],"translations":{}}]"""
                .Replace("QUOTE_ID", quoteId));
        File.WriteAllText(rulesPath,
            """{"rules":[{"entityId":"QUOTE_ID","existingRecord":{"quoteText":"Original text."},"incomingRecord":{"quoteText":"Changed text."},"fields":[{"field":"quoteText","resolution":"Keep"}]}]}"""
                .Replace("QUOTE_ID", quoteId));

        SeedBatch batch = new SeedBatch(
            [
                new SeedFile(baselinePath, null, Policy: new ManifestPolicy(DuplicateResolutionPolicy.NewestWins)),
                new SeedFile(conflictPath, null, Policy: new ManifestPolicy(DuplicateResolutionPolicy.Review), RuleFilePath: rulesPath),
            ],
            ManifestPolicy.HardcodedDefault, "rule-file-test");

        QuotinatorDatabaseInitializer db = CreateInitializer([batch]);
        await db.InitialiseAsync();

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);

        Assert.AreEqual(0, (await new ImportActionReader(new SqliteConnectionFactory(_dbPath)).GetPagedAsync(null, "Pending", null, 1, 0)).TotalCount,
            "The rule fully covers the only ambiguous field — nothing should be left Pending");
        Assert.AreEqual("Original text.", await conn.ExecuteScalarAsync<string>("SELECT QuoteText FROM Quotinator_Quote WHERE Id = @id;", new { id = quoteId }),
            "Keep must resolve to the existing (baseline) value");
    }

    /// <summary>
    /// #153: end-to-end proof that a registered, hash-verified override on the persistent volume is
    /// preferred over the bundled rule file the manifest actually references — the override says
    /// Replace where the bundled copy says Keep, and the applied result reflects Replace.
    /// </summary>
    [TestMethod]
    public async Task InitialiseAsync_RegisteredOverrideWithMatchingHash_IsPreferredOverBundledRuleFile()
    {
        const string quoteId = "d3111111-1111-4111-8111-111111111111";
        string baselinePath = Path.Combine(_tempDir, "override-baseline.json");
        string conflictPath = Path.Combine(_tempDir, "override-conflict.json");
        string bundledRulesPath = Path.Combine(_tempDir, "override-conflict-rules.json");

        File.WriteAllText(baselinePath,
            """[{"id":"QUOTE_ID","quote":"Original text.","originalLanguage":"en","source":"Test Film","date":"2000","character":null,"author":null,"type":"movie","genres":[],"translations":{}}]"""
                .Replace("QUOTE_ID", quoteId));
        File.WriteAllText(conflictPath,
            """[{"id":"QUOTE_ID","quote":"Changed text.","originalLanguage":"en","source":"Test Film","date":"2000","character":null,"author":null,"type":"movie","genres":[],"translations":{}}]"""
                .Replace("QUOTE_ID", quoteId));
        // Bundled copy says Keep — if this were used, the applied text would stay "Original text.".
        File.WriteAllText(bundledRulesPath,
            """{"rules":[{"entityId":"QUOTE_ID","existingRecord":{"quoteText":"Original text."},"incomingRecord":{"quoteText":"Changed text."},"fields":[{"field":"quoteText","resolution":"Keep"}]}]}"""
                .Replace("QUOTE_ID", quoteId));

        string internalDownloadDir = Path.Combine(_tempDir, "sources", "download");
        RuleFileOverridePathResolver pathResolver = new RuleFileOverridePathResolver(internalDownloadDir, Path.Combine(_tempDir, "imports", "download"));
        string overridePath = pathResolver.Resolve(Path.GetFileName(bundledRulesPath), SeedBatchOrigin.Bundled);
        Directory.CreateDirectory(Path.GetDirectoryName(overridePath)!);
        // Override says Replace — the applied text must come from here instead.
        string overrideContent =
            """{"rules":[{"entityId":"QUOTE_ID","existingRecord":{"quoteText":"Original text."},"incomingRecord":{"quoteText":"Changed text."},"fields":[{"field":"quoteText","resolution":"Replace"}]}]}"""
                .Replace("QUOTE_ID", quoteId);
        File.WriteAllText(overridePath, overrideContent);

        SourceFileOverrideRegistry registry = new SourceFileOverrideRegistry(new SqliteConnectionFactory(_dbPath));
        SeedBatch batch = new SeedBatch(
            [
                new SeedFile(baselinePath, null, Policy: new ManifestPolicy(DuplicateResolutionPolicy.NewestWins)),
                new SeedFile(conflictPath, null, Policy: new ManifestPolicy(DuplicateResolutionPolicy.Review), RuleFilePath: bundledRulesPath),
            ],
            ManifestPolicy.HardcodedDefault, "override-test");

        // The registry table must exist before InitialiseAsync runs migrations — the very first
        // write in this test — so seed a bare, migration-free database via a throwaway initializer
        // first, matching how every other test in this file lets InitialiseAsync create the schema.
        await CreateInitializer([batch]).InitialiseAsync();
        await registry.RegisterAsync(Path.GetFileName(bundledRulesPath), SeedBatchOrigin.Bundled,
            EffectiveRuleFileResolver.ComputeContentHash(overrideContent), sourceBatchId: null, TestContext.CancellationToken);

        QuotinatorDatabaseInitializer db = CreateInitializer([batch], ruleFileOverridePathResolver: pathResolver, sourceFileOverrideRegistry: registry);
        await db.ReseedAsync();

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);

        Assert.AreEqual("Changed text.", await conn.ExecuteScalarAsync<string>("SELECT QuoteText FROM Quotinator_Quote WHERE Id = @id;", new { id = quoteId }),
            "The registered override (Replace) must win over the bundled rule file (Keep)");
    }

    /// <summary>
    /// #153: an override file physically present on disk but never registered (or registered under a
    /// stale hash) must never be silently trusted — falls back to the bundled rule file exactly as if
    /// no override existed at all.
    /// </summary>
    [TestMethod]
    public async Task InitialiseAsync_OverrideFileWithoutMatchingRegistration_FallsBackToBundledRuleFile()
    {
        const string quoteId = "d4111111-1111-4111-8111-111111111111";
        string baselinePath = Path.Combine(_tempDir, "unregistered-baseline.json");
        string conflictPath = Path.Combine(_tempDir, "unregistered-conflict.json");
        string bundledRulesPath = Path.Combine(_tempDir, "unregistered-conflict-rules.json");

        File.WriteAllText(baselinePath,
            """[{"id":"QUOTE_ID","quote":"Original text.","originalLanguage":"en","source":"Test Film","date":"2000","character":null,"author":null,"type":"movie","genres":[],"translations":{}}]"""
                .Replace("QUOTE_ID", quoteId));
        File.WriteAllText(conflictPath,
            """[{"id":"QUOTE_ID","quote":"Changed text.","originalLanguage":"en","source":"Test Film","date":"2000","character":null,"author":null,"type":"movie","genres":[],"translations":{}}]"""
                .Replace("QUOTE_ID", quoteId));
        File.WriteAllText(bundledRulesPath,
            """{"rules":[{"entityId":"QUOTE_ID","existingRecord":{"quoteText":"Original text."},"incomingRecord":{"quoteText":"Changed text."},"fields":[{"field":"quoteText","resolution":"Keep"}]}]}"""
                .Replace("QUOTE_ID", quoteId));

        string internalDownloadDir = Path.Combine(_tempDir, "sources2", "download");
        RuleFileOverridePathResolver pathResolver = new RuleFileOverridePathResolver(internalDownloadDir, Path.Combine(_tempDir, "imports2", "download"));
        string overridePath = pathResolver.Resolve(Path.GetFileName(bundledRulesPath), SeedBatchOrigin.Bundled);
        Directory.CreateDirectory(Path.GetDirectoryName(overridePath)!);
        // An override file exists on disk, but is never registered below.
        File.WriteAllText(overridePath,
            """{"rules":[{"entityId":"QUOTE_ID","existingRecord":{"quoteText":"Original text."},"incomingRecord":{"quoteText":"Changed text."},"fields":[{"field":"quoteText","resolution":"Replace"}]}]}"""
                .Replace("QUOTE_ID", quoteId));

        SourceFileOverrideRegistry registry = new SourceFileOverrideRegistry(new SqliteConnectionFactory(_dbPath));
        SeedBatch batch = new SeedBatch(
            [
                new SeedFile(baselinePath, null, Policy: new ManifestPolicy(DuplicateResolutionPolicy.NewestWins)),
                new SeedFile(conflictPath, null, Policy: new ManifestPolicy(DuplicateResolutionPolicy.Review), RuleFilePath: bundledRulesPath),
            ],
            ManifestPolicy.HardcodedDefault, "unregistered-override-test");

        QuotinatorDatabaseInitializer db = CreateInitializer([batch], ruleFileOverridePathResolver: pathResolver, sourceFileOverrideRegistry: registry);
        await db.InitialiseAsync();

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);

        Assert.AreEqual("Original text.", await conn.ExecuteScalarAsync<string>("SELECT QuoteText FROM Quotinator_Quote WHERE Id = @id;", new { id = quoteId }),
            "An unregistered override file must never be trusted — the bundled rule file (Keep) must be used instead");
    }

    /// <summary>Regression guard: the same scenario with no rule file at all must behave exactly as before #181 — Pending, nothing overwritten.</summary>
    [TestMethod]
    public async Task InitialiseAsync_SecondFileReviewPolicyNoRuleFile_StagesPendingAsBefore()
    {
        const string quoteId = "d2111111-1111-4111-8111-111111111111";
        string baselinePath = Path.Combine(_tempDir, "baseline2.json");
        string conflictPath = Path.Combine(_tempDir, "conflict2.json");

        File.WriteAllText(baselinePath,
            """[{"id":"QUOTE_ID","quote":"Original text.","originalLanguage":"en","source":"Test Film","date":"2000","character":null,"author":null,"type":"movie","genres":[],"translations":{}}]"""
                .Replace("QUOTE_ID", quoteId));
        File.WriteAllText(conflictPath,
            """[{"id":"QUOTE_ID","quote":"Changed text.","originalLanguage":"en","source":"Test Film","date":"2000","character":null,"author":null,"type":"movie","genres":[],"translations":{}}]"""
                .Replace("QUOTE_ID", quoteId));

        SeedBatch batch = new SeedBatch(
            [
                new SeedFile(baselinePath, null, Policy: new ManifestPolicy(DuplicateResolutionPolicy.NewestWins)),
                new SeedFile(conflictPath, null, Policy: new ManifestPolicy(DuplicateResolutionPolicy.Review)),
            ],
            ManifestPolicy.HardcodedDefault, "no-rule-file-test");

        QuotinatorDatabaseInitializer db = CreateInitializer([batch]);
        await db.InitialiseAsync();

        Assert.AreEqual(1, (await new ImportActionReader(new SqliteConnectionFactory(_dbPath)).GetPagedAsync(null, "Pending", null, 1, 0)).TotalCount,
            "No rule file was referenced — behaviour must be unchanged from before #181");
    }

    /// <summary>
    /// End-to-end proof of #181's source-title alias mechanism: a second file's quote references a
    /// misspelled Source title that an alias file maps to the first file's already-established
    /// canonical Source — must resolve to that one Source, never create a duplicate.
    /// </summary>
    [TestMethod]
    public async Task InitialiseAsync_SecondFileMisspelledSourceWithMatchingAlias_ResolvesToExistingSourceNoDuplicate()
    {
        string canonicalPath = Path.Combine(_tempDir, "canonical.json");
        string misspeltPath  = Path.Combine(_tempDir, "misspelt.json");
        string aliasPath     = Path.Combine(_tempDir, "source-aliases.json");

        File.WriteAllText(canonicalPath,
            """[{"id":"e1111111-1111-4111-8111-111111111111","quote":"First quote.","originalLanguage":"en","source":"The Avengers","date":"2012","character":null,"author":null,"type":"movie","genres":[],"translations":{}}]""");
        File.WriteAllText(misspeltPath,
            """[{"id":"e2222222-2222-4222-8222-222222222222","quote":"Second quote.","originalLanguage":"en","source":"Marvel's The Avengers","date":"2012","character":null,"author":null,"type":"movie","genres":[],"translations":{}}]""");
        File.WriteAllText(aliasPath,
            """{"aliases":[{"title":"Marvel's The Avengers","type":"movie","canonicalTitle":"The Avengers","canonicalType":"movie"}]}""");

        SeedBatch batch = new SeedBatch(
            [
                new SeedFile(canonicalPath, null, Policy: new ManifestPolicy(DuplicateResolutionPolicy.NewestWins)),
                new SeedFile(misspeltPath, null, Policy: new ManifestPolicy(DuplicateResolutionPolicy.NewestWins), SourceAliasFilePath: aliasPath),
            ],
            ManifestPolicy.HardcodedDefault, "source-alias-test");

        QuotinatorDatabaseInitializer db = CreateInitializer([batch]);
        await db.InitialiseAsync();

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);

        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_Source WHERE Title = 'The Avengers';"),
            "The alias must resolve the misspelled title to the already-existing canonical Source — no duplicate");
        Assert.AreEqual(2, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_Quote;"), "Both quotes must still be seeded");
    }

    /// <summary>No source files configured — database is created but stays empty.</summary>
    [TestMethod]
    public async Task InitialiseAsync_EmptyBatches_DatabaseIsEmpty()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([]);
        await db.InitialiseAsync();

        Assert.AreEqual(0, db.QuoteCount);
    }

    /// <summary>Calling InitialiseAsync a second time on an already-seeded database is a no-op.</summary>
    [TestMethod]
    public async Task InitialiseAsync_CalledTwice_IsIdempotent()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        int countAfterFirst = db.QuoteCount;
        await db.InitialiseAsync();

        Assert.AreEqual(countAfterFirst, db.QuoteCount);
    }

    // ── Reset (#156: full wipe + baseline rebuild, no reseed, supersedes #141) ─────────────────

    /// <summary>ResetAsync on an already-seeded database drops and recreates all tables at the empty baseline — it no longer reimports bundled/user content.</summary>
    [TestMethod]
    public async Task ResetAsync_AfterInitialise_RebuildsSchemaAndDoesNotReseed()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        Assert.IsGreaterThan(0, db.QuoteCount, "Sanity check — initial seed must have produced quotes");

        await db.ResetAsync();

        Assert.AreEqual(0, db.QuoteCount, "Reset's one job is rebuilding the schema to empty — it must not reimport bundled/user content (#156)");
    }

    private const string MarkerValue = "manual-test-marker";

    // #155: distinct markers for each of the 4 rows a genuine v1.7.2 legacy SchemaVersion table
    // holds (InitialSchema, ReseedGenres, ImportBatches, CreateAuditEntriesTable), so a test can
    // confirm exactly which row ends up in which of the two new counters after the split.
    private const string LegacyV1Marker = "legacy-v1-initial-schema";
    private const string LegacyV2Marker = "legacy-v2-reseed-genres";
    private const string LegacyV3Marker = "legacy-v3-import-batches";
    private const string LegacyV4Marker = "legacy-v4-create-audit-entries-table";

    /// <summary>A full Reset is a full wipe — Audit_Entry no longer survives, reversing #141's preserve-on-reset behaviour per #156/ADR 014 (an operator who wants to keep it exports it first via the admin audit export endpoint, #249).</summary>
    [TestMethod]
    public async Task ResetAsync_AfterInitialise_WipesExistingAuditEntries()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        await InsertAuditMarkerAsync();

        await db.ResetAsync();

        Assert.AreEqual(0, await CountAuditMarkerRowsAsync(), "Full Reset must wipe existing Audit_Entry rows — no protected-table concept remains (#156)");
    }

    /// <summary>With the default parameter, Reset now also clears and replays System_SchemaVersion — Quotinator.Data's own tables are no longer excluded from the wipe (#156).</summary>
    [TestMethod]
    public async Task ResetAsync_DefaultParameter_AlsoReplaysDataSchemaVersion()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        await InsertSchemaVersionMarkerAsync();

        await db.ResetAsync(preserveSchemaVersion: false);

        Assert.AreEqual(0, await CountSchemaVersionMarkerRowsAsync(),
            "Default Reset should clear and replay System_SchemaVersion too now, removing the pre-existing marker row");
    }

    /// <summary>With preserveSchemaVersion:true, Reset now also leaves existing System_SchemaVersion rows untouched — symmetric with the consumer's own counter, since both are wiped by the full-database drop.</summary>
    [TestMethod]
    public async Task ResetAsync_PreserveSchemaVersionTrue_AlsoKeepsExistingDataVersionRows()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        await InsertSchemaVersionMarkerAsync();

        await db.ResetAsync(preserveSchemaVersion: true);

        Assert.AreEqual(1, await CountSchemaVersionMarkerRowsAsync(),
            "preserveSchemaVersion:true should leave existing System_SchemaVersion rows untouched too");
    }

    /// <summary>With the default parameter, Reset still clears and replays System_ConsumerSchemaVersion — unchanged historical behaviour for the consumer's own migrations.</summary>
    [TestMethod]
    public async Task ResetAsync_DefaultParameter_StillReplaysConsumerSchemaVersion()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        await InsertConsumerSchemaVersionMarkerAsync();

        await db.ResetAsync();

        Assert.AreEqual(0, await CountConsumerSchemaVersionMarkerRowsAsync(),
            "Default Reset should clear and replay System_ConsumerSchemaVersion, removing the pre-existing marker row");
    }

    /// <summary>With preserveSchemaVersion:true, Reset leaves existing System_ConsumerSchemaVersion rows untouched.</summary>
    [TestMethod]
    public async Task ResetAsync_PreserveSchemaVersionTrue_KeepsExistingConsumerVersionRows()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        await InsertConsumerSchemaVersionMarkerAsync();

        await db.ResetAsync(preserveSchemaVersion: true);

        Assert.AreEqual(1, await CountConsumerSchemaVersionMarkerRowsAsync(),
            "preserveSchemaVersion:true should leave existing System_ConsumerSchemaVersion rows untouched");
    }

    /// <summary>Reseed (not Reset) has always left Audit_Entry and System_SchemaVersion alone — this makes that behaviour explicit.</summary>
    [TestMethod]
    public async Task ReseedAsync_AfterInitialise_LeavesAuditEntriesAndSchemaVersionUntouched()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        await InsertAuditMarkerAsync();
        await InsertSchemaVersionMarkerAsync();

        await db.ReseedAsync();

        Assert.AreEqual(1, await CountAuditMarkerRowsAsync(),        "Reseed must not touch Audit_Entry");
        Assert.AreEqual(1, await CountSchemaVersionMarkerRowsAsync(), "Reseed must not touch System_SchemaVersion");
    }

    private async Task InsertAuditMarkerAsync()
    {
        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);
        await conn.ExecuteAsync(
            "INSERT INTO Audit_Entry (Id, TableName, RecordId, Operation, Agent, PerformedAt, DateCreated) " +
            "VALUES (lower(hex(randomblob(16))), 'Quotes', 'test-id', 'Insert', @marker, '2026-01-01 00:00:00', '2026-01-01 00:00:00');",
            new { marker = MarkerValue });
    }

    private async Task<int> CountAuditMarkerRowsAsync()
    {
        IReadOnlyList<AuditEntryEntity> entries = (await new AuditEntryReader(new SqliteConnectionFactory(_dbPath)).GetPagedAsync("Quotes", null, 1, 0)).Items;
        return entries.Count(e => e.Agent == MarkerValue);
    }

    private async Task InsertSchemaVersionMarkerAsync()
    {
        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);
        await conn.ExecuteAsync(
            "INSERT INTO System_SchemaVersion (Version, AppliedAt) VALUES (1, @marker);", new { marker = MarkerValue });
    }

    private async Task<int> CountSchemaVersionMarkerRowsAsync()
    {
        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM System_SchemaVersion WHERE AppliedAt = @marker;", new { marker = MarkerValue });
    }

    private async Task InsertConsumerSchemaVersionMarkerAsync()
    {
        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);
        await conn.ExecuteAsync(
            "INSERT INTO System_ConsumerSchemaVersion (Version, AppliedAt) VALUES (1, @marker);", new { marker = MarkerValue });
    }

    private async Task<int> CountConsumerSchemaVersionMarkerRowsAsync()
    {
        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM System_ConsumerSchemaVersion WHERE AppliedAt = @marker;", new { marker = MarkerValue });
    }

    // ── Full-wipe table discovery (#156, supersedes #141's protected-table concept) ────────────

    /// <summary>
    /// GetAllTables returns literally every real table, with no exclusion of any kind — #156
    /// retired the System_/Import_/Audit_ protected-table concept GetUserTables used to implement,
    /// since Reset is now a full, unconditional wipe.
    /// </summary>
    [TestMethod]
    public async Task GetAllTables_ReturnsEveryTableRegardlessOfPrefix()
    {
        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);
        await conn.ExecuteAsync("CREATE TABLE System_FooBar (Id INTEGER);");
        await conn.ExecuteAsync("CREATE TABLE Import_FooBar (Id INTEGER);");
        await conn.ExecuteAsync("CREATE TABLE Audit_FooBar (Id INTEGER);");
        await conn.ExecuteAsync("CREATE TABLE FooBar (Id INTEGER);");

        List<string> tables = [.. (await conn.QueryAsync<string>(Sql.Schema.GetAllTables))];

        Assert.Contains("System_FooBar", tables, "System_-prefixed tables must no longer be excluded");
        Assert.Contains("Import_FooBar", tables, "Import_-prefixed tables must no longer be excluded");
        Assert.Contains("Audit_FooBar", tables, "Audit_-prefixed tables must no longer be excluded");
        Assert.Contains("FooBar", tables, "Non-prefixed tables must still be included");
    }

    /// <summary>A fresh database creates System_SchemaVersion directly — it is never created under the legacy name and then renamed.</summary>
    [TestMethod]
    public async Task InitialiseAsync_FreshDatabase_CreatesSystemSchemaVersionDirectly()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([]);
        await db.InitialiseAsync();

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);
        int legacyCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'SchemaVersion';");
        int systemCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'System_SchemaVersion';");

        Assert.AreEqual(0, legacyCount, "A fresh database must never contain a table literally named SchemaVersion");
        Assert.AreEqual(1, systemCount, "A fresh database must create System_SchemaVersion directly");
    }

    /// <summary>
    /// Builds a fully up-to-date database, then downgrades it back to a genuine v1.7.2 legacy shape:
    /// a single unified <c>SchemaVersion</c> table holding exactly the 4 rows that release actually
    /// shipped (InitialSchema, ReseedGenres, ImportBatches, CreateAuditEntriesTable — confirmed
    /// directly against the `main` branch's own code, not assumed), plus the legacy <c>AuditEntries</c>
    /// table shape. Both new counter tables are cleared first so the split has a genuinely empty
    /// target to populate, matching what a real v1.7.2 database's tables looked like before the
    /// #143 split existed at all. See #155 — this replaces an earlier version of this helper that
    /// used a single, arbitrary <c>Version = 1</c> row, which incidentally never exercised the real
    /// bug (the legacy rename silently skipping Data migrations 2-4 by numeric coincidence with the
    /// real, 4-row legacy value).
    /// </summary>
    private async Task DowngradeToLegacyNamesAsync()
    {
        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);
        await conn.ExecuteAsync("DELETE FROM System_SchemaVersion;");
        await conn.ExecuteAsync("DELETE FROM System_ConsumerSchemaVersion;");
        await conn.ExecuteAsync("ALTER TABLE System_SchemaVersion RENAME TO SchemaVersion;");
        await conn.ExecuteAsync(
            "INSERT INTO SchemaVersion (Version, AppliedAt) VALUES " +
            "(1, @m1), (2, @m2), (3, @m3), (4, @m4);",
            new { m1 = LegacyV1Marker, m2 = LegacyV2Marker, m3 = LegacyV3Marker, m4 = LegacyV4Marker });

        // Rebuild AuditEntries under its true migration-1 legacy shape (auto-increment long Id, no
        // RecordBase columns) rather than a bare rename — a bare rename would carry over migration
        // 5's RecordBase columns (added after this test's InitialiseAsync() call already ran the
        // full migration chain), which didn't exist in a genuinely pre-migration-2 database.
        await conn.ExecuteAsync("""
            CREATE TABLE AuditEntries (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                TableName   TEXT    NOT NULL,
                RecordId    TEXT,
                Operation   TEXT    NOT NULL,
                Agent       TEXT,
                PerformedAt TEXT    NOT NULL
            );
            """);
        await conn.ExecuteAsync(
            "INSERT INTO AuditEntries (TableName, RecordId, Operation, Agent, PerformedAt) " +
            "SELECT TableName, RecordId, Operation, Agent, PerformedAt FROM Audit_Entry;");
        await conn.ExecuteAsync("DROP TABLE Audit_Entry;");
        await conn.ExecuteAsync("CREATE INDEX IX_AuditEntries_TableName_RecordId ON AuditEntries (TableName, RecordId);");
        await conn.ExecuteAsync("CREATE INDEX IX_AuditEntries_PerformedAt ON AuditEntries (PerformedAt);");

        // #253: Audit_Change/Import_Conflict/Import_Action/Import_SourceFileOverride are all created
        // by migration 2 (SinceV172), not migration 1 — a genuinely pre-migration-2 database has none
        // of them yet. Migration 2's own CREATE TABLE IF NOT EXISTS statements are idempotent against
        // a table that never existed under their old (pre-rename) names, but migration 3's ALTER
        // TABLE ... RENAME TO is not — it fails outright if the final name is already taken. Dropping
        // these four here (instead of just leaving them under their post-#253 final names) is what
        // makes replaying migrations 2+3 from this fixture safe, the same way the AuditEntries rebuild
        // above is.
        await conn.ExecuteAsync("DROP TABLE Audit_Change;");
        await conn.ExecuteAsync("DROP TABLE Import_Conflict;");
        await conn.ExecuteAsync("DROP TABLE Import_Action;");
        await conn.ExecuteAsync("DROP TABLE Import_SourceFileOverride;");

        // #312/#81: same reasoning as the four drops above, for the two tables migrations 3-5 own.
        // This fixture reaches its "legacy" state by running the *full* migration chain and then
        // undoing it, so without these drops System_Notification still carries migration 5's already-
        // renamed Body column — and migration 5's ALTER TABLE ... RENAME COLUMN Message TO Body is no
        // more idempotent than migration 3's RENAME TO, failing outright on replay with
        // 'no such column: "Message"'. Dropping both restores a genuinely pre-migration-3 state.
        // System_AppVersion goes too: migration 4 creates it, and migration 5's AppVersionId FK
        // references it.
        await conn.ExecuteAsync("DROP TABLE IF EXISTS System_Notification;");
        await conn.ExecuteAsync("DROP TABLE IF EXISTS System_AppVersion;");
    }

    /// <summary>
    /// #155 regression guard: a genuine v1.7.2 legacy <c>SchemaVersion</c> table (4 rows — Init/
    /// ReseedGenres/ImportBatches/CreateAuditEntriesTable) must split correctly — versions 1-3 into
    /// <c>System_ConsumerSchemaVersion</c> (each timestamp preserved), version 4 renumbered to 1 in
    /// <c>System_SchemaVersion</c> (also preserved) — <em>not</em> a bare rename that copies the raw
    /// value 4 straight into Data's counter. The original bug this guards against: with a bare
    /// rename, <c>dataCurrent</c> reads 4 immediately, and since Data's own migrations 2-4 today
    /// (<c>RenameAuditEntriesToSystemAuditEntries</c>, <c>CreateImportConflictsTable</c>,
    /// <c>CreateChangeLogTable</c>) numerically coincide with that value, all three were silently
    /// skipped as "already applied" even though none had ever actually run — leaving
    /// <c>AuditEntries</c> never renamed and <c>System_ImportConflicts</c>/<c>System_ChangeLog</c>
    /// never created, while <c>DataSchemaVersion</c> still reported "fully up to date" once the
    /// later migrations ran. This test's own table-existence assertions are the direct proof the fix
    /// closes that gap, not just that the version counters end up numerically correct.
    /// </summary>
    [TestMethod]
    public async Task InitialiseAsync_LegacyV172SchemaVersionTable_SplitsCorrectlyAndReplaysRemainingMigrations()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([]);
        await db.InitialiseAsync();
        await DowngradeToLegacyNamesAsync();

        // This database's domain tables are already fully migrated (from the InitialiseAsync() call
        // above), so exercising a genuine replay of Consumer's own migrations 4-11 against them
        // would hit real column/table conflicts — that end-to-end scenario against a truly
        // legacy-shaped v1.7.2 database is step 5's job (a real git-worktree snapshot), not this
        // unit test's. Passing an empty Consumer migration list here means Consumer has nothing to
        // replay against, so nothing can conflict; Data's own fixed migration list still applies
        // unconditionally regardless of what's passed here, so this still fully exercises the bug
        // this test guards against.
        QuotinatorDatabaseInitializer db2 = CreateInitializer([], migrations: [], useBaseline: false);
        await db2.InitialiseAsync();

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);

        int legacyCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'SchemaVersion';");
        List<(int Version, string AppliedAt)> consumerRows = [.. (await conn.QueryAsync<(int Version, string AppliedAt)>(
            "SELECT Version, AppliedAt FROM System_ConsumerSchemaVersion ORDER BY Version;"))];
        List<(int Version, string AppliedAt)> dataRows = [.. (await conn.QueryAsync<(int Version, string AppliedAt)>(
            "SELECT Version, AppliedAt FROM System_SchemaVersion ORDER BY Version;"))];

        Assert.AreEqual(0, legacyCount, "The legacy SchemaVersion table must no longer exist after the split");

        Assert.HasCount(3, consumerRows, "Legacy versions 1-3 must land in System_ConsumerSchemaVersion, unrenumbered");
        Assert.AreEqual((1, LegacyV1Marker), consumerRows[0]);
        Assert.AreEqual((2, LegacyV2Marker), consumerRows[1]);
        Assert.AreEqual((3, LegacyV3Marker), consumerRows[2]);

        // dataRows also includes migration 2's own row by this point (db2.InitialiseAsync()
        // already replayed it, in the same call that ran the split) — only row 1 is under test
        // here: it must carry legacy version 4's original marker, proving the split renumbered it
        // to 1 rather than leaving it at its raw legacy value of 4 (which, pre-#155, the
        // then-separate migrations 2-4 would have read as "already applied" and skipped — the
        // original bug).
        Assert.AreEqual(LegacyV4Marker, dataRows.Single(r => r.Version == 1).AppliedAt);

        // The actual bug symptom: these three tables must exist and be queryable — a bare rename
        // left them permanently missing on a real v1.7.2 upgrade despite DataSchemaVersion claiming
        // "up to date".
        foreach (string? table in new[] { "Audit_Entry", "Import_Conflict", "Audit_Change" })
        {
            int tableExists = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @table;", new { table });
            Assert.AreEqual(1, tableExists, $"{table} must exist after replaying the remaining Data migrations from a correctly-seeded starting point");
        }

        // Derived, not a literal: the claim is that the replay carried on past the seeded starting point
        // of 1 and that the reported version matches what was actually recorded. A hardcoded number
        // states neither, goes stale on the next migration, and invites a digit edit instead of a check.
        Assert.IsGreaterThan(1, db2.DataSchemaVersion,
            "Every Data-owned migration after the first should have replayed from the correctly-seeded starting point of 1");
        Assert.AreEqual(dataRows.Max(row => row.Version), db2.DataSchemaVersion,
            "The reported version must match the highest version actually recorded in System_SchemaVersion");
    }

    /// <summary>Replaying from a legacy v1.7.2 AuditEntries table renames it all the way to Audit_Entry (via migration 2's Audit_Entry then migration 3's domain-prefix rename) and preserves existing rows and both indexes.</summary>
    [TestMethod]
    public async Task InitialiseAsync_LegacyAuditEntriesTable_MigratesToAuditEntryWithRowsPreserved()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([]);
        await db.InitialiseAsync();
        await DowngradeToLegacyNamesAsync();

        using (SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await conn.OpenAsync(TestContext.CancellationToken);
            await conn.ExecuteAsync(
                "INSERT INTO AuditEntries (TableName, RecordId, Operation, Agent, PerformedAt) " +
                "VALUES ('Quotes', 'test-id', 'Insert', @marker, '2026-01-01 00:00:00');",
                new { marker = MarkerValue });
        }

        // Empty Consumer migration list — see InitialiseAsync_LegacyV172SchemaVersionTable_... above
        // for why: this database's domain tables are already fully migrated, so a genuine replay of
        // Consumer's own migrations would hit real column/table conflicts unrelated to what this
        // test is actually about (Data migration 2's AuditEntries rename).
        QuotinatorDatabaseInitializer db2 = CreateInitializer([], migrations: [], useBaseline: false);
        await db2.InitialiseAsync();

        using SqliteConnection verifyConn = new SqliteConnection($"Data Source={_dbPath}");
        await verifyConn.OpenAsync(TestContext.CancellationToken);
        int legacyCount = await verifyConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'AuditEntries';");
        int preservedRow = await verifyConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Audit_Entry WHERE Agent = @marker;", new { marker = MarkerValue });
        List<string> indexNames = [.. (await verifyConn.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'Audit_Entry';"))];

        Assert.AreEqual(0, legacyCount, "The legacy AuditEntries table must no longer exist after replay");
        Assert.AreEqual(1, preservedRow, "The pre-existing audit row must survive the rename");
        Assert.Contains("IX_Audit_Entry_TableName_RecordId", indexNames, "TableName/RecordId index must exist under the final name");
        Assert.Contains("IX_Audit_Entry_PerformedAt", indexNames, "PerformedAt index must exist under the final name");
    }

    // ── Regression ────────────────────────────────────────────────────────────

    /// <summary>
    /// Regression test for issue #106: if the App schema version is rolled back to v2 while the
    /// underlying tables already have v3 columns (ImportBatchId), the recorded version no longer
    /// matches the actual schema — a genuine anomaly, not something InitialiseAsync should ever
    /// silently guess its way through. It must fail loudly (no structural check, no message-matching
    /// recovery), leave the database exactly as it was before the attempt (backup restored), and
    /// require an explicit Reset to resolve. Uses the forced-incremental path so App migrations are
    /// recorded one row per version (the baseline path records a single row, leaving nothing to roll
    /// back to "v2" from).
    /// </summary>
    /// <remarks>
    /// Deletes every version row from 3 upward, not just "the last few" — <c>GetConsumerCurrentVersion</c>
    /// computes <c>MAX(Version)</c>, not row count, so leaving any higher-numbered row in place (e.g.
    /// deleting only 3 and 4) would leave the computed version at whatever the highest remaining row is
    /// and InitialiseAsync would see nothing pending to replay, defeating the whole scenario. Deleting 3
    /// upward drops MAX back to 2, reproducing the original #106 scenario regardless of how many
    /// migrations now exist above it.
    /// </remarks>
    [TestMethod]
    public async Task InitialiseAsync_PartialMigrationState_FailsSafelyAndRequiresExplicitReset()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseForTestingAsync(forceIncremental: true);

        int countAfterInit = db.QuoteCount;

        using (SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await conn.OpenAsync(TestContext.CancellationToken);
            await conn.ExecuteAsync("DELETE FROM System_ConsumerSchemaVersion WHERE Version >= 3;");
        }

        QuotinatorDatabaseInitializer db2 = CreateInitializer([AllFilesBatch()]);
        await Assert.ThrowsExactlyAsync<SqliteException>(() => db2.InitialiseAsync());

        using (SqliteConnection verifyConn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await verifyConn.OpenAsync(TestContext.CancellationToken);
            int quoteCountAfterFailedAttempt = await verifyConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_Quote;");
            Assert.AreEqual(countAfterInit, quoteCountAfterFailedAttempt,
                "Database must be restored to its pre-attempt state after a failed migration, not left partially migrated");
        }

        QuotinatorDatabaseInitializer db3 = CreateInitializer([AllFilesBatch()]);
        await db3.ResetAsync();
        Assert.AreEqual(5, db3.SchemaVersion, "An explicit Reset must fully resolve the version/schema mismatch");
    }

    /// <summary>
    /// Found live during #254's own T1 pass: migration version tracking only sees a pending
    /// migration when the recorded count is behind the current count — rewriting an unreleased
    /// migration's content in place (same slot, same final count) leaves an already-migrated-once
    /// database reading as "up to date" even though its actual on-disk schema no longer matches what
    /// the new content produces. Migrations skip cleanly in that case (nothing pending, no backup
    /// needed), but seeding runs unconditionally on every startup (a cheap existence/count check even
    /// when there is nothing to seed) and has no equivalent "is this even safe to attempt" signal to
    /// key off — the mismatch can only surface once the check actually queries the live tables. Before
    /// this fix, that left <c>OnInitialisedAsync</c> with zero exception safety net, unlike the
    /// migration phase's own backup/restore/rethrow. This test doesn't reproduce the exact version-
    /// count blind spot (that requires two different migration *contents* under the same *count*,
    /// awkward to construct here) — it reproduces the general class the fix actually covers: seeding
    /// throwing on an already-migrated (non-baseline) database, for any reason.
    /// </summary>
    [TestMethod]
    public async Task InitialiseAsync_SeedingFailsOnAlreadyMigratedDatabase_BacksUpFirstAndRethrows()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();
        Assert.IsGreaterThan(0, db.QuoteCount, "Precondition: the first InitialiseAsync call must have actually seeded data");

        using (SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await conn.OpenAsync(TestContext.CancellationToken);
            await conn.ExecuteAsync("PRAGMA foreign_keys = OFF;");
            await conn.ExecuteAsync("DROP TABLE Quotinator_Quote;");
        }

        Directory.CreateDirectory(_backups);
        int backupCountBefore = Directory.GetFiles(_backups, "*.db").Length;

        QuotinatorDatabaseInitializer db2 = CreateInitializer([AllFilesBatch()]);
        await Assert.ThrowsExactlyAsync<SqliteException>(() => db2.InitialiseAsync());

        int backupCountAfter = Directory.GetFiles(_backups, "*.db").Length;
        Assert.AreEqual(backupCountBefore + 1, backupCountAfter,
            "A seeding failure on an already-migrated (non-baseline) database must take exactly one backup before attempting to seed");
    }

    // ── #143 — migration ownership split + baseline schema ─────────────────────

    private (QuotinatorDatabaseInitializer Db, string DbPath) CreateForcedIncrementalInitializer()
    {
        string dbPath        = Path.Combine(_tempDir, $"test_incremental_{Guid.NewGuid():N}.db");
        SqliteConnectionFactory factory       = new SqliteConnectionFactory(dbPath);
        DatabaseOptions options       = new DatabaseOptions { DbPath = dbPath, BackupsPath = _backups };
        SqliteImportBatchRepository importBatches = new SqliteImportBatchRepository(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance);
        ImportActionReader actionReader  = new ImportActionReader(factory);
        ImportActionWriter actionWriter  = new ImportActionWriter(factory);
        ImportActionResolutionCoordinator coordinator   = new ImportActionResolutionCoordinator(actionReader, actionWriter, factory);
        SqliteImportActionService actionService = new SqliteImportActionService(actionReader, coordinator, actionWriter, NoOpAuditEntryWriter.Instance, NoOpChangeWriter.Instance,
            new SqliteRestorableRepository<QuoteEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<SourceEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<CharacterEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<PersonEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<ConversationEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<StageDirectionEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<SoundCueEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            importBatches, factory, NoOpNotificationWriter.Instance);
        QuotinatorDatabaseInitializer db = new QuotinatorDatabaseInitializer(factory, options, QuotinatorMigrations.All, [], importBatches,
            coordinator, actionService, actionWriter,
            NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance, NullLogger<DatabaseInitializer>.Instance,
            NoOpSourceCacheUpdater.Instance, autoUpdateSources: false,
            autoPurgeBundledImportActions: false, autoPurgeUserImportActions: false,
            NoOpRuleFileOverridePathResolver.Instance, NoOpSourceFileOverrideRegistry.Instance,
            NoOpFileResourceRepository.Instance,
            NoOpNotificationReader.Instance, NoOpNotificationWriter.Instance, NoOpNotificationTextSource.Instance,
            new AppVersionTracker(factory), new VersionService(), NoOpDiskSpaceProvider.Instance,
            QuotinatorMigrations.Baseline);
        return (db, dbPath);
    }

    private static async Task<List<string>> DumpTableSchemaAsync(SqliteConnection conn, string table)
    {
        List<string> lines = [];

        IEnumerable<(int cid, string name, string type, int notnull, string? dflt_value, int pk)> columns = await conn.QueryAsync<(int cid, string name, string type, int notnull, string? dflt_value, int pk)>(
            $"SELECT cid, name, type, [notnull], dflt_value, pk FROM pragma_table_info('{table}');");
        foreach ((int cid, string? name, string? type, int notnull, string? dflt_value, int pk) in columns.OrderBy(c => c.cid))
            lines.Add($"COL {cid} {name} {type} notnull={notnull} default={dflt_value} pk={pk}");

        IEnumerable<(string name, int unique)> indexes = await conn.QueryAsync<(string name, int unique)>(
            $"SELECT name, [unique] FROM pragma_index_list('{table}');");
        foreach ((string? name, int unique) in indexes.OrderBy(i => i.name))
        {
            IEnumerable<(int seqno, string? name)> idxCols = await conn.QueryAsync<(int seqno, string? name)>(
                $"SELECT seqno, name FROM pragma_index_info('{name}');");
            string colList = string.Join(",", idxCols.OrderBy(c => c.seqno).Select(c => c.name));
            lines.Add($"IDX {name} unique={unique} cols=({colList})");
        }

        return lines;
    }

    private static readonly string[] ConsumerDomainTables =
        ["Import_Batch", "Quotinator_Source", "Quotinator_SourceTranslation", "Quotinator_Character", "Quotinator_CharacterTranslation",
         "Quotinator_Person", "Quotinator_Quote", "Quotinator_QuoteTranslation", "Quotinator_QuoteGenre",
         "Quotinator_Conversation", "Quotinator_ConversationLine", "Quotinator_StageDirection", "Quotinator_StageDirectionTranslation",
         "Quotinator_SoundCue", "Quotinator_SoundCueTranslation",
         "Quotinator_Universe", "Quotinator_Series", "Quotinator_CharacterSource"];

    /// <summary>
    /// QuotinatorMigrations.Baseline must produce the exact same schema, table by table, as
    /// replaying QuotinatorMigrations.All incrementally. Comparison uses PRAGMA table_info/
    /// index_list/index_info rather than raw sqlite_master text, since hand-formatted baseline SQL
    /// and migration-assembled SQL (e.g. Sources' ImportBatchId appended via ALTER TABLE) would
    /// differ textually even when semantically identical.
    /// </summary>
    [TestMethod]
    public async Task Baseline_And_IncrementalReplay_ProduceIdenticalConsumerSchema()
    {
        QuotinatorDatabaseInitializer dbA = CreateInitializer([]);
        await dbA.InitialiseAsync();

        (QuotinatorDatabaseInitializer dbB, string dbPathB) = CreateForcedIncrementalInitializer();
        await dbB.InitialiseForTestingAsync(forceIncremental: true);

        using SqliteConnection connA = new SqliteConnection($"Data Source={_dbPath}");
        await connA.OpenAsync(TestContext.CancellationToken);
        using SqliteConnection connB = new SqliteConnection($"Data Source={dbPathB}");
        await connB.OpenAsync(TestContext.CancellationToken);

        foreach (string table in ConsumerDomainTables)
        {
            List<string> schemaA = await DumpTableSchemaAsync(connA, table);
            List<string> schemaB = await DumpTableSchemaAsync(connB, table);
            Assert.AreSequenceEqual(schemaB, schemaA, $"Table '{table}' schema differs between the baseline and incremental paths — " +
                "update QuotinatorMigrations.Baseline to match QuotinatorMigrations.All's final result.");
        }
    }

    /// <summary>
    /// PRAGMA table_info/index_list do not capture CHECK constraint text, so a baseline that
    /// silently dropped 'UserSeed' from ImportBatches.Type's constraint (or introduced a typo)
    /// would pass the structural schema comparison above undetected. This behavioural round-trip
    /// closes that gap for all three CHECK-constrained columns.
    /// </summary>
    [TestMethod]
    public async Task Baseline_And_IncrementalReplay_AcceptSameCheckConstraintValues()
    {
        QuotinatorDatabaseInitializer dbA = CreateInitializer([]);
        await dbA.InitialiseAsync();

        (QuotinatorDatabaseInitializer dbB, string dbPathB) = CreateForcedIncrementalInitializer();
        await dbB.InitialiseForTestingAsync(forceIncremental: true);

        using SqliteConnection connA = new SqliteConnection($"Data Source={_dbPath}");
        await connA.OpenAsync(TestContext.CancellationToken);
        using SqliteConnection connB = new SqliteConnection($"Data Source={dbPathB}");
        await connB.OpenAsync(TestContext.CancellationToken);

        foreach (SqliteConnection? conn in new[] { connA, connB })
        {
            // QuoteGenres.QuoteId is a FK to Quotes(Id) — irrelevant to the CHECK constraint being
            // tested here, so disable enforcement rather than seed a matching Quotes row.
            await conn.ExecuteAsync("PRAGMA foreign_keys = OFF;");

            string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            await conn.ExecuteAsync(
                "INSERT INTO Import_Batch (Id, Name, Type, ImportedAt, RecordCount, DateCreated, IsDeleted) " +
                "VALUES (@id, 'check-test.json', 'UserSeed', @now, 0, @now, 0);",
                new { id = Guid.NewGuid().ToString(), now });

            await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
                "INSERT INTO Import_Batch (Id, Name, Type, ImportedAt, RecordCount, DateCreated, IsDeleted) " +
                "VALUES (@id, 'bad.json', 'NotARealType', @now, 0, @now, 0);",
                new { id = Guid.NewGuid().ToString(), now }));

            // #150, ADR 008: ImportBatches.ConflictPolicy's CHECK constraint.
            await conn.ExecuteAsync(
                "INSERT INTO Import_Batch (Id, Name, Type, ImportedAt, RecordCount, DateCreated, IsDeleted, ConflictPolicy) " +
                "VALUES (@id, 'check-test-policy.json', 'Import', @now, 0, @now, 0, 'NewestWins');",
                new { id = Guid.NewGuid().ToString(), now });

            await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
                "INSERT INTO Import_Batch (Id, Name, Type, ImportedAt, RecordCount, DateCreated, IsDeleted, ConflictPolicy) " +
                "VALUES (@id, 'bad-policy.json', 'Import', @now, 0, @now, 0, 'NotARealPolicy');",
                new { id = Guid.NewGuid().ToString(), now }));

            await conn.ExecuteAsync(
                "INSERT INTO Import_Batch (Id, Name, Type, ImportedAt, RecordCount, DateCreated, IsDeleted, Status) " +
                "VALUES (@id, 'check-test-staged.json', 'Import', @now, 0, @now, 0, 'Staged');",
                new { id = Guid.NewGuid().ToString(), now });

            await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
                "INSERT INTO Import_Batch (Id, Name, Type, ImportedAt, RecordCount, DateCreated, IsDeleted, Status) " +
                "VALUES (@id, 'bad-status.json', 'Import', @now, 0, @now, 0, 'NotARealStatus');",
                new { id = Guid.NewGuid().ToString(), now }));

            await conn.ExecuteAsync(
                "INSERT INTO Quotinator_Source (Id, Title, Type, DateCreated, IsDeleted) VALUES (@id, 'CheckTest', 'Person', @now, 0);",
                new { id = Guid.NewGuid().ToString(), now });

            await conn.ExecuteAsync(
                "INSERT INTO Quotinator_QuoteGenre (Id, QuoteId, Genre, DateCreated, IsDeleted) " +
                "VALUES (@id, @quoteId, 'SciFi', @now, 0);",
                new { id = Guid.NewGuid().ToString(), quoteId = Guid.NewGuid().ToString(), now });

            // ConversationLines carries two independent CHECK constraints (#67): a simple
            // LineType-membership CHECK (ADR 008) and a separate cross-field CHECK enforcing that
            // exactly the FK matching LineType is populated. Both are exercised here.
            string quoteLineId = Guid.NewGuid().ToString();
            await conn.ExecuteAsync(
                "INSERT INTO Quotinator_ConversationLine (Id, ConversationId, [Order], LineType, QuoteId, DateCreated, IsDeleted) " +
                "VALUES (@id, @conversationId, 1, 'Quote', @quoteId, @now, 0);",
                new { id = quoteLineId, conversationId = Guid.NewGuid().ToString(), quoteId = Guid.NewGuid().ToString(), now });

            await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
                "INSERT INTO Quotinator_ConversationLine (Id, ConversationId, [Order], LineType, QuoteId, DateCreated, IsDeleted) " +
                "VALUES (@id, @conversationId, 2, 'NotARealLineType', @quoteId, @now, 0);",
                new { id = Guid.NewGuid().ToString(), conversationId = Guid.NewGuid().ToString(), quoteId = Guid.NewGuid().ToString(), now }));

            await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
                "INSERT INTO Quotinator_ConversationLine (Id, ConversationId, [Order], LineType, StageDirectionId, DateCreated, IsDeleted) " +
                "VALUES (@id, @conversationId, 3, 'Quote', @stageDirectionId, @now, 0);",
                new { id = Guid.NewGuid().ToString(), conversationId = Guid.NewGuid().ToString(), stageDirectionId = Guid.NewGuid().ToString(), now }));
        }
    }

    // ── #67 — Conversations schema ──────────────────────────────────────────────

    private static readonly string[] ConversationTablesWithRecordBase =
        ["Quotinator_Conversation", "Quotinator_ConversationLine", "Quotinator_StageDirection", "Quotinator_StageDirectionTranslation",
         "Quotinator_SoundCue", "Quotinator_SoundCueTranslation"];

    /// <summary>Every table added by #67 carries RecordBase's four audit columns — ADR 002 applies without exception, including the line/junction table and both translation tables.</summary>
    [TestMethod]
    public async Task ConversationTables_AllHaveRecordBaseColumns()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([]);
        await db.InitialiseAsync();

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);

        foreach (string table in ConversationTablesWithRecordBase)
        {
            HashSet<string> columns = (await conn.QueryAsync<string>(
                $"SELECT name FROM pragma_table_info('{table}');")).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (string? recordBaseColumn in new[] { "Id", "DateCreated", "DateModified", "DateDeleted", "IsDeleted" })
                Assert.Contains(recordBaseColumn, columns, $"{table} is missing RecordBase column {recordBaseColumn}");
        }
    }

    /// <summary><c>UNIQUE (ConversationId, Order)</c> rejects a second line at an already-used position.</summary>
    [TestMethod]
    public async Task ConversationLines_UniqueConstraint_RejectsDuplicateOrder()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([]);
        await db.InitialiseAsync();

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);
        await conn.ExecuteAsync("PRAGMA foreign_keys = OFF;");

        string now            = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        string conversationId = Guid.NewGuid().ToString();

        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_ConversationLine (Id, ConversationId, [Order], LineType, QuoteId, DateCreated, IsDeleted) " +
            "VALUES (@id, @conversationId, 1, 'Quote', @quoteId, @now, 0);",
            new { id = Guid.NewGuid().ToString(), conversationId, quoteId = Guid.NewGuid().ToString(), now });

        await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
            "INSERT INTO Quotinator_ConversationLine (Id, ConversationId, [Order], LineType, QuoteId, DateCreated, IsDeleted) " +
            "VALUES (@id, @conversationId, 1, 'Quote', @quoteId, @now, 0);",
            new { id = Guid.NewGuid().ToString(), conversationId, quoteId = Guid.NewGuid().ToString(), now }));
    }

    /// <summary><c>UNIQUE (StageDirectionId, Language)</c> and <c>UNIQUE (SoundCueId, Language)</c> reject a second translation in the same language.</summary>
    [TestMethod]
    public async Task TranslationTables_UniqueConstraint_RejectsDuplicateLanguage()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([]);
        await db.InitialiseAsync();

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);
        await conn.ExecuteAsync("PRAGMA foreign_keys = OFF;");

        string now              = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        string stageDirectionId = Guid.NewGuid().ToString();
        string soundCueId       = Guid.NewGuid().ToString();

        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_StageDirectionTranslation (Id, StageDirectionId, Language, Text, DateCreated, IsDeleted) " +
            "VALUES (@id, @stageDirectionId, 'nl', 'Tekst', @now, 0);",
            new { id = Guid.NewGuid().ToString(), stageDirectionId, now });

        await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
            "INSERT INTO Quotinator_StageDirectionTranslation (Id, StageDirectionId, Language, Text, DateCreated, IsDeleted) " +
            "VALUES (@id, @stageDirectionId, 'nl', 'Andere tekst', @now, 0);",
            new { id = Guid.NewGuid().ToString(), stageDirectionId, now }));

        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_SoundCueTranslation (Id, SoundCueId, Language, Text, DateCreated, IsDeleted) " +
            "VALUES (@id, @soundCueId, 'nl', 'Tekst', @now, 0);",
            new { id = Guid.NewGuid().ToString(), soundCueId, now });

        await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
            "INSERT INTO Quotinator_SoundCueTranslation (Id, SoundCueId, Language, Text, DateCreated, IsDeleted) " +
            "VALUES (@id, @soundCueId, 'nl', 'Andere tekst', @now, 0);",
            new { id = Guid.NewGuid().ToString(), soundCueId, now }));
    }

    /// <summary><see cref="ConversationLineType"/> round-trips through Dapper as a real enum, not an int — the <see cref="Quotinator.Data.Helpers.SafeEnumHandler{TEnum}"/> pattern already used for <see cref="Quotinator.Data.Enums.ImportBatchType"/>/<see cref="Quotinator.Data.Enums.ImportBatchStatus"/>.</summary>
    [TestMethod]
    public async Task ConversationLineType_RoundTripsThroughDapper()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([]);
        await db.InitialiseAsync();

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);
        await conn.ExecuteAsync("PRAGMA foreign_keys = OFF;");

        Guid lineId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_ConversationLine (Id, ConversationId, [Order], LineType, StageDirectionId, DateCreated, IsDeleted) " +
            "VALUES (@id, @conversationId, 1, 'StageDirection', @stageDirectionId, @now, 0);",
            new
            {
                id             = lineId.ToString(),
                conversationId = Guid.NewGuid().ToString(),
                stageDirectionId = Guid.NewGuid().ToString(),
                now            = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            });

        ConversationLineEntity line = await conn.QuerySingleAsync<ConversationLineEntity>(
            "SELECT * FROM Quotinator_ConversationLine WHERE Id = @id;", new { id = lineId.ToString() });

        Assert.AreEqual(ConversationLineType.StageDirection, line.LineType.Parsed);
    }

    /// <summary>A fresh (zero-table) database takes the baseline path — both version tables end up with exactly one row each, at the final version.</summary>
    [TestMethod]
    public async Task InitialiseAsync_TrulyEmptyDatabase_TakesBaselinePathNotIncremental()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([]);
        await db.InitialiseAsync();

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);
        int dataRows     = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM System_SchemaVersion;");
        int consumerRows = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM System_ConsumerSchemaVersion;");

        Assert.AreEqual(1, dataRows,     "Baseline path should insert exactly one row into System_SchemaVersion");
        Assert.AreEqual(1, consumerRows, "Baseline path should insert exactly one row into System_ConsumerSchemaVersion");
        // The claim is that one collapsed row still reports the fully-migrated version, not that the
        // version is any particular number — a literal here goes stale whenever a milestone adds a
        // migration and gets "fixed" by editing the digit rather than by rechecking the collapse.
        Assert.IsGreaterThan(0, db.DataSchemaVersion,
            "The baseline must report a real version, or the single collapsed row above proves nothing.");
        Assert.AreEqual(5, db.SchemaVersion);
    }

    /// <summary>
    /// #289: a recorded version higher than this build's own known migration count (the state a
    /// migration squash produces on a database that already applied the pre-squash migrations) is
    /// treated as already-complete, not an error — no exception, the real recorded (higher) version is
    /// reported rather than the smaller known count, and the overshoot is flagged for the caller.
    /// </summary>
    [TestMethod]
    public async Task InitialiseAsync_RecordedVersionExceedsKnownMigrations_TreatsAsUpToDateAndFlagsOvershoot()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([]);
        await db.InitialiseAsync();
        Assert.IsFalse(db.SchemaVersionOvershootDetected, "Sanity check — a normal fresh database has no overshoot");

        using (SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await conn.OpenAsync(TestContext.CancellationToken);
            await conn.ExecuteAsync(
                "INSERT INTO System_ConsumerSchemaVersion (Version, AppliedAt) VALUES (@v, @at);",
                new { v = QuotinatorMigrations.All.Count + 1, at = "2026-01-01T00:00:00Z" });
        }

        QuotinatorDatabaseInitializer db2 = CreateInitializer([]);
        await db2.InitialiseAsync();

        Assert.IsTrue(db2.SchemaVersionOvershootDetected, "A recorded version ahead of the known migration list must be flagged");
        Assert.AreEqual(QuotinatorMigrations.All.Count + 1, db2.SchemaVersion,
            "The real recorded (higher) version must be reported, not silently replaced by the smaller known count");
    }

    /// <summary>#289: an ordinary, correctly-migrated database never flags an overshoot.</summary>
    [TestMethod]
    public async Task InitialiseAsync_NoOvershoot_FlagStaysFalse()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([]);
        await db.InitialiseAsync();
        Assert.IsFalse(db.SchemaVersionOvershootDetected);

        QuotinatorDatabaseInitializer db2 = CreateInitializer([]);
        await db2.InitialiseAsync();
        Assert.IsFalse(db2.SchemaVersionOvershootDetected, "A second, already-up-to-date startup must not flag an overshoot either");
    }

    /// <summary>
    /// A database created before the #143 migration-ownership split has a single System_SchemaVersion
    /// table holding the old combined history (one row per migration, spanning both Data's and the
    /// consumer's migrations together — 13 rows for the schema this test targets: 7 Data + 6 consumer),
    /// with no System_ConsumerSchemaVersion table at all yet. This recorded state doesn't match the
    /// actual on-disk schema (which already has the consumer's columns), so ordinary startup must fail
    /// loudly — no structural check, no message-matching recovery — leaving the database exactly as
    /// it was before the attempt (backup restored). An explicit Reset is the only sanctioned way to
    /// resolve it.
    /// </summary>
    [TestMethod]
    public async Task InitialiseAsync_PreSplitCombinedCounterDatabase_FailsSafelyAndRequiresExplicitReset()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();
        int quoteCountBefore = db.QuoteCount;

        using (SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await conn.OpenAsync(TestContext.CancellationToken);
            await conn.ExecuteAsync("DROP TABLE System_ConsumerSchemaVersion;");
            await conn.ExecuteAsync("DELETE FROM System_SchemaVersion;");
            for (int v = 1; v <= 13; v++)
                await conn.ExecuteAsync(
                    "INSERT INTO System_SchemaVersion (Version, AppliedAt) VALUES (@v, @at);",
                    new { v, at = $"2026-01-01T00:00:{v:D2}Z" });
        }

        QuotinatorDatabaseInitializer db2 = CreateInitializer([AllFilesBatch()]);
        await Assert.ThrowsExactlyAsync<SqliteException>(() => db2.InitialiseAsync());

        using (SqliteConnection verifyConn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await verifyConn.OpenAsync(TestContext.CancellationToken);
            int quoteCountAfterFailedAttempt = await verifyConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_Quote;");
            Assert.AreEqual(quoteCountBefore, quoteCountAfterFailedAttempt,
                "Database must be restored to its pre-attempt state after the failed startup, not left partially migrated");
        }

        QuotinatorDatabaseInitializer db3 = CreateInitializer([AllFilesBatch()]);
        await db3.ResetAsync();
        Assert.AreEqual(5, db3.SchemaVersion, "An explicit Reset must fully resolve the mismatch");
    }

    // ── #179 — Series/Universe schema, Character↔Source many-to-many ───────────

    /// <summary>Migration009 adds Universe and Series, both insertable/readable, with Series.UniverseId nullable.</summary>
    [TestMethod]
    public async Task Migration_SeriesUniverseSchema_AddsUniverseAndSeriesTables()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([]);
        await db.InitialiseAsync();

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        string universeId = Guid.NewGuid().ToString();
        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_Universe (Id, Name, DateCreated, IsDeleted, CompletenessStatus, NoValueKnown) " +
            "VALUES (@id, 'Middle Earth', @now, 0, 'Incomplete', '[]');",
            new { id = universeId, now });

        string seriesId = Guid.NewGuid().ToString();
        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_Series (Id, Name, UniverseId, DateCreated, IsDeleted, CompletenessStatus, NoValueKnown) " +
            "VALUES (@id, 'The Lord of the Rings', @universeId, @now, 0, 'Incomplete', '[]');",
            new { id = seriesId, universeId, now });

        string standaloneSeriesId = Guid.NewGuid().ToString();
        await conn.ExecuteAsync(
            "INSERT INTO Quotinator_Series (Id, Name, UniverseId, DateCreated, IsDeleted, CompletenessStatus, NoValueKnown) " +
            "VALUES (@id, 'Some Standalone Series', NULL, @now, 0, 'Incomplete', '[]');",
            new { id = standaloneSeriesId, now });

        Assert.AreEqual(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_Universe WHERE Id = @id;", new { id = universeId }));
        Assert.AreEqual(2, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_Series;"));
        Assert.AreEqual(universeId, await conn.ExecuteScalarAsync<string>("SELECT UniverseId FROM Quotinator_Series WHERE Id = @id;", new { id = seriesId }));
    }

    /// <summary>Migration009 drops Characters.SourceId and its old UNIQUE(SourceId, Name) constraint.</summary>
    [TestMethod]
    public async Task Migration_SeriesUniverseSchema_DropsCharactersSourceIdColumn()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([]);
        await db.InitialiseAsync();

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);

        HashSet<string> columns = (await conn.QueryAsync<string>(
            "SELECT name FROM pragma_table_info('Quotinator_Character');")).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("SourceId", columns, "Characters.SourceId must be dropped by Migration009");

        IEnumerable<string> indexes = await conn.QueryAsync<string>("SELECT name FROM pragma_index_list('Quotinator_Character') WHERE [unique] = 1;");
        foreach (string idx in indexes)
        {
            List<string> idxCols = [.. (await conn.QueryAsync<string>($"SELECT name FROM pragma_index_info('{idx}');"))];
            Assert.DoesNotContain("SourceId", idxCols, StringComparer.OrdinalIgnoreCase,
                $"Index '{idx}' still references SourceId — the old UNIQUE(SourceId, Name) constraint must be gone");
        }
    }

    // ── #174: Migration011_CharacterGlobalIdentity (ADR 013) ────────────────────

    /// <summary>
    /// #155: builds the pre-#174 precondition state directly rather than via a partial-migration
    /// checkpoint — since consolidated migration 4 now fuses Series/CharacterSources schema creation
    /// and the character-merge logic into one atomic, non-reentrant migration, there is no longer any
    /// reachable migration-boundary between "schema exists" and "merge has run" to stop at (this was
    /// already true of any *real* upgrade even before consolidation — nothing in migration 4 itself
    /// ever populates Sources.SeriesId, only the app's own later import/seeding path does, so the
    /// merge only ever found real candidates against a database that had *already* progressed through
    /// real usage between two separate release cycles; per #155, no release ever shipped these
    /// migrations separately, so that in-between state never existed in reality either).
    /// <para/>
    /// Per #155: never pass a truncated migration list to a <c>DatabaseInitializer</c>. Building the
    /// v3-equivalent schema here therefore doesn't use <c>CreateInitializer</c>/<c>InitialiseAsync</c>
    /// at all — it executes the three real, frozen Consumer migrations directly against a raw
    /// connection (the same technique <c>ImportBatchesTests</c>' rename test uses), then, as this
    /// class's own precondition doc above explains, no real migration replay can ever reach the exact
    /// moment this test needs (Sources.SeriesId populated but the merge not yet run — a state that
    /// cannot exist for any real upgrading user, since both now happen in the same atomic migration).
    /// So the two migration 4 fragments are likewise executed directly, as the specific pieces of real
    /// production SQL they are, to unit-test <c>CharacterGlobalIdentityMerge</c>'s own logic in
    /// isolation against a hand-built but structurally realistic precondition — never through
    /// <c>CreateInitializer</c>, truncated or otherwise.
    /// </summary>
    private async Task<(string source1Id, string source2Id, string character1Id, string character2Id)> SeedPreMergeCharactersAsync(
        string name = "Gandalf", string? name2 = null, string type1 = "Movie", string type2 = "Movie",
        string? seriesId1 = null, string? seriesId2 = null,
        string completeness1 = "Incomplete", string completeness2 = "Incomplete",
        string dateCreated1 = "2026-01-01 00:00:00", string dateCreated2 = "2026-01-02 00:00:00")
    {
        name2 ??= name;

        string source1Id    = Guid.NewGuid().ToString();
        string source2Id    = Guid.NewGuid().ToString();
        string character1Id = Guid.NewGuid().ToString();
        string character2Id = Guid.NewGuid().ToString();

        using (SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await conn.OpenAsync(TestContext.CancellationToken);
            await conn.ExecuteAsync(QuotinatorMigrations.Migration001_InitialSchema);
            await conn.ExecuteAsync(QuotinatorMigrations.Migration002_ReseedGenres);
            await conn.ExecuteAsync(QuotinatorMigrations.Migration003_ImportBatches);

            await conn.ExecuteAsync(
                "INSERT INTO Sources (Id, Title, Type, DateCreated, IsDeleted) VALUES (@id, @title, @type, @now, 0);",
                new { id = source1Id, title = "Source One", type = type1, now = dateCreated1 });
            await conn.ExecuteAsync(
                "INSERT INTO Sources (Id, Title, Type, DateCreated, IsDeleted) VALUES (@id, @title, @type, @now, 0);",
                new { id = source2Id, title = "Source Two", type = type2, now = dateCreated2 });

            await conn.ExecuteAsync(
                "INSERT INTO Characters (Id, SourceId, Name, DateCreated, IsDeleted) VALUES (@id, @sourceId, @name, @now, 0);",
                new { id = character1Id, sourceId = source1Id, name, now = dateCreated1 });
            await conn.ExecuteAsync(
                "INSERT INTO Characters (Id, SourceId, Name, DateCreated, IsDeleted) VALUES (@id, @sourceId, @name, @now, 0);",
                new { id = character2Id, sourceId = source2Id, name = name2, now = dateCreated2 });

            // Migration009's own Characters rebuild carries CompletenessStatus across from the
            // pre-existing row — but that column doesn't exist yet at true v3 (it's added by an
            // earlier part of migration 4 than the rebuild, applied moments from now). Set it
            // directly after the schema-creation portion runs, before the merge portion reads it.
            await conn.ExecuteAsync(QuotinatorMigrations.Migration004_ConsolidatedSinceV172Core);

            await conn.ExecuteAsync(
                "UPDATE Characters SET CompletenessStatus = @completeness WHERE Id = @id;",
                new { id = character1Id, completeness = completeness1 });
            await conn.ExecuteAsync(
                "UPDATE Characters SET CompletenessStatus = @completeness WHERE Id = @id;",
                new { id = character2Id, completeness = completeness2 });

            // Sources.SeriesId is never populated by any migration itself — only the app's own later
            // import/seeding path does this in reality. Simulate that here for whichever Sources this
            // specific test wants linked into a (real or shared) Series.
            foreach (string? seriesId in new[] { seriesId1, seriesId2 }.Where(s => s is not null).Distinct())
                await conn.ExecuteAsync(
                    "INSERT OR IGNORE INTO Series (Id, Name, DateCreated, IsDeleted) VALUES (@id, @name, @now, 0);",
                    new { id = seriesId, name = $"Series {seriesId}", now = dateCreated1 });
            if (seriesId1 is not null)
                await conn.ExecuteAsync("UPDATE Sources SET SeriesId = @seriesId WHERE Id = @id;", new { id = source1Id, seriesId = seriesId1 });
            if (seriesId2 is not null)
                await conn.ExecuteAsync("UPDATE Sources SET SeriesId = @seriesId WHERE Id = @id;", new { id = source2Id, seriesId = seriesId2 });

            await conn.ExecuteAsync(QuotinatorMigrations.CharacterGlobalIdentityMerge);
        }

        return (source1Id, source2Id, character1Id, character2Id);
    }

    [TestMethod]
    public async Task Migration_CharacterGlobalIdentity_ConsolidatesSameNameRowsWithinKnownSeries()
    {
        string seriesId = Guid.NewGuid().ToString();
        (string _, string _, string? character1Id, string? character2Id) =
            await SeedPreMergeCharactersAsync(seriesId1: seriesId, seriesId2: seriesId);

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);

        int survivorCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Characters WHERE Name = 'Gandalf' AND IsDeleted = 0;");
        Assert.AreEqual(1, survivorCount, "Two same-named Characters whose Sources share a Series must consolidate into one row");

        string? survivorId = await conn.ExecuteScalarAsync<string>(
            "SELECT Id FROM Characters WHERE Name = 'Gandalf' AND IsDeleted = 0;");
        Assert.AreEqual(character1Id, survivorId, "The earlier-DateCreated row must survive");

        int linkedSourceCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM CharacterSources WHERE CharacterId = @id AND IsDeleted = 0;", new { id = survivorId });
        Assert.AreEqual(2, linkedSourceCount, "The survivor must carry links to both original Sources");

        int mergedAwayDeleted = await conn.ExecuteScalarAsync<int>(
            "SELECT IsDeleted FROM Characters WHERE Id = @id;", new { id = character2Id });
        Assert.AreEqual(1, mergedAwayDeleted, "The merged-away row must be soft-deleted, never hard-deleted");
    }

    /// <summary>
    /// Character storage always preserves the exact casing a Name was originally written with, but the
    /// merge-candidate comparison itself is case-insensitive (confirmed directly by the developer
    /// during ADR 013's authoring — corrects an initial draft that wrongly extended Sources.Title's
    /// case-sensitive precedent to Character).
    /// </summary>
    [TestMethod]
    public async Task Migration_CharacterGlobalIdentity_MergesDespiteDifferingNameCasing()
    {
        string seriesId = Guid.NewGuid().ToString();
        (string _, string _, string? character1Id, string? character2Id) = await SeedPreMergeCharactersAsync(
            name: "Gandalf", name2: "GANDALF", seriesId1: seriesId, seriesId2: seriesId);

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);

        int survivorCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Characters WHERE LOWER(Name) = 'gandalf' AND IsDeleted = 0;");
        Assert.AreEqual(1, survivorCount, "Differing casing of the same Name must still merge — only storage preserves original casing, not the comparison");

        string? survivorName = await conn.ExecuteScalarAsync<string>(
            "SELECT Name FROM Characters WHERE Id = @id;", new { id = character1Id });
        Assert.AreEqual("Gandalf", survivorName, "The surviving row's own original casing must be preserved, never rewritten to match the merged-away row's casing");

        int mergedAwayDeleted = await conn.ExecuteScalarAsync<int>(
            "SELECT IsDeleted FROM Characters WHERE Id = @id;", new { id = character2Id });
        Assert.AreEqual(1, mergedAwayDeleted);
    }

    [TestMethod]
    public async Task Migration_CharacterGlobalIdentity_NeverMergesAcrossDifferingSourceType()
    {
        string seriesId = Guid.NewGuid().ToString();
        await SeedPreMergeCharactersAsync(name: "Gandalf", type1: "Movie", type2: "Book",
            seriesId1: seriesId, seriesId2: seriesId);

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);

        int survivorCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Characters WHERE Name = 'Gandalf' AND IsDeleted = 0;");
        Assert.AreEqual(2, survivorCount, "A shared Series must never override the Source.Type anchor invariant (ADR 011)");
    }

    [TestMethod]
    public async Task Migration_CharacterGlobalIdentity_LeavesUnrelatedSameNameRowsUnmergedWhenNoSeriesKnown()
    {
        await SeedPreMergeCharactersAsync(name: "Sam", seriesId1: null, seriesId2: null);

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);

        int survivorCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Characters WHERE Name = 'Sam' AND IsDeleted = 0;");
        Assert.AreEqual(2, survivorCount, "Same Name, same Type, but no known Series relationship — conservative default must leave both rows separate");
    }

    /// <summary>
    /// Per #155: no <c>CreateInitializer</c>/<c>InitialiseAsync</c> call anywhere in this test, same
    /// reasoning as <see cref="SeedPreMergeCharactersAsync"/> above — the real, frozen Consumer
    /// migrations build the base schema, migration 4's schema-creation fragment builds the
    /// Series/CharacterSources shape, then <see cref="QuotinatorMigrations.CharacterGlobalIdentityMerge"/>
    /// runs directly to unit-test its own quote-repointing behaviour against a hand-built precondition
    /// that (as documented above) can never arise from a real migration replay.
    /// </summary>
    [TestMethod]
    public async Task Migration_CharacterGlobalIdentity_RepointsQuoteCharacterIdToMergedRow()
    {
        string seriesId = Guid.NewGuid().ToString();

        string source1Id    = Guid.NewGuid().ToString();
        string source2Id    = Guid.NewGuid().ToString();
        string character1Id = Guid.NewGuid().ToString();
        string character2Id = Guid.NewGuid().ToString();
        string quoteId       = Guid.NewGuid().ToString();

        using (SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await conn.OpenAsync(TestContext.CancellationToken);
            await conn.ExecuteAsync(QuotinatorMigrations.Migration001_InitialSchema);
            await conn.ExecuteAsync(QuotinatorMigrations.Migration002_ReseedGenres);
            await conn.ExecuteAsync(QuotinatorMigrations.Migration003_ImportBatches);
            await conn.ExecuteAsync(QuotinatorMigrations.Migration004_ConsolidatedSinceV172Core);

            await conn.ExecuteAsync(
                "INSERT INTO Series (Id, Name, DateCreated, IsDeleted) VALUES (@id, @name, '2026-01-01 00:00:00', 0);",
                new { id = seriesId, name = $"Series {seriesId}" });
            await conn.ExecuteAsync(
                "INSERT INTO Sources (Id, Title, Type, SeriesId, DateCreated, IsDeleted) VALUES (@id, 'Source One', 'Movie', @seriesId, '2026-01-01 00:00:00', 0);",
                new { id = source1Id, seriesId });
            await conn.ExecuteAsync(
                "INSERT INTO Sources (Id, Title, Type, SeriesId, DateCreated, IsDeleted) VALUES (@id, 'Source Two', 'Movie', @seriesId, '2026-01-02 00:00:00', 0);",
                new { id = source2Id, seriesId });
            await conn.ExecuteAsync(
                "INSERT INTO Characters (Id, Name, CompletenessStatus, DateCreated, IsDeleted) VALUES (@id, 'Gandalf', 'Incomplete', '2026-01-01 00:00:00', 0);",
                new { id = character1Id });
            await conn.ExecuteAsync(
                "INSERT INTO Characters (Id, Name, CompletenessStatus, DateCreated, IsDeleted) VALUES (@id, 'Gandalf', 'Incomplete', '2026-01-02 00:00:00', 0);",
                new { id = character2Id });
            await conn.ExecuteAsync(
                "INSERT INTO CharacterSources (Id, CharacterId, SourceId, DateCreated, IsDeleted) VALUES (@id, @characterId, @sourceId, '2026-01-01 00:00:00', 0);",
                new { id = Guid.NewGuid().ToString(), characterId = character1Id, sourceId = source1Id });
            await conn.ExecuteAsync(
                "INSERT INTO CharacterSources (Id, CharacterId, SourceId, DateCreated, IsDeleted) VALUES (@id, @characterId, @sourceId, '2026-01-02 00:00:00', 0);",
                new { id = Guid.NewGuid().ToString(), characterId = character2Id, sourceId = source2Id });
            await conn.ExecuteAsync(
                "INSERT INTO Quotes (Id, QuoteText, OriginalLanguage, SourceId, CharacterId, DateCreated, IsDeleted) VALUES (@id, 'A line.', 'en', @sourceId, @characterId, '2026-01-02 00:00:00', 0);",
                new { id = quoteId, sourceId = source2Id, characterId = character2Id });

            await conn.ExecuteAsync(QuotinatorMigrations.CharacterGlobalIdentityMerge);
        }

        using SqliteConnection verifyConn = new SqliteConnection($"Data Source={_dbPath}");
        await verifyConn.OpenAsync(TestContext.CancellationToken);

        string? resolvedCharacterId = await verifyConn.ExecuteScalarAsync<string>(
            "SELECT CharacterId FROM Quotes WHERE Id = @id;", new { id = quoteId });
        Assert.AreEqual(character1Id, resolvedCharacterId, "The quote's CharacterId must be re-pointed to the surviving row, not left dangling on the merged-away one");
    }

    [TestMethod]
    public async Task Migration_CharacterGlobalIdentity_PreservesCompletenessStatusPerAlgorithm()
    {
        string seriesId = Guid.NewGuid().ToString();
        (string _, string _, string? character1Id, string _) = await SeedPreMergeCharactersAsync(
            seriesId1: seriesId, seriesId2: seriesId,
            completeness1: "Incomplete", completeness2: "Complete");

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);

        string? survivorStatus = await conn.ExecuteScalarAsync<string>(
            "SELECT CompletenessStatus FROM Characters WHERE Id = @id;", new { id = character1Id });
        Assert.AreEqual("Complete", survivorStatus, "The most-reviewed CompletenessStatus across the merged group must win");
    }

    [TestMethod]
    public async Task Migration_CharacterGlobalIdentity_BackfillsSourceTypeColumnFromLinkedSource()
    {
        (string _, string _, string? character1Id, string? character2Id) = await SeedPreMergeCharactersAsync(
            name: "Sam", type1: "Movie", type2: "Book", seriesId1: null, seriesId2: null);

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);

        string? type1 = await conn.ExecuteScalarAsync<string>("SELECT SourceType FROM Characters WHERE Id = @id;", new { id = character1Id });
        string? type2 = await conn.ExecuteScalarAsync<string>("SELECT SourceType FROM Characters WHERE Id = @id;", new { id = character2Id });
        Assert.AreEqual("Movie", type1);
        Assert.AreEqual("Book", type2);
    }

    /// <summary>Every table added by #179 carries RecordBase's four audit columns — ADR 002 applies without exception, including the CharacterSources junction table.</summary>
    [TestMethod]
    public async Task SeriesUniverseTables_AllHaveRecordBaseColumns()
    {
        QuotinatorDatabaseInitializer db = CreateInitializer([]);
        await db.InitialiseAsync();

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);

        foreach (string? table in new[] { "Quotinator_Universe", "Quotinator_Series", "Quotinator_CharacterSource" })
        {
            HashSet<string> columns = (await conn.QueryAsync<string>(
                $"SELECT name FROM pragma_table_info('{table}');")).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (string? recordBaseColumn in new[] { "Id", "DateCreated", "DateModified", "DateDeleted", "IsDeleted" })
                Assert.Contains(recordBaseColumn, columns, $"{table} is missing RecordBase column {recordBaseColumn}");
        }
    }

    // ── #180: curated series/universe overlay — end-to-end through the real bundled-seed path ──
    // Unlike SqliteQuoteImportService.ImportAsync (the live POST /import endpoint), the bundled
    // seed path's own LoadSourceFileAsync has no "at least one quote" requirement — appropriate
    // here since this overlay file's whole purpose is Source/Series/Universe enrichment with an
    // intentionally empty quotes[] section, matching data/sources/quotinator-series-universe.json's
    // own real shape.
    //
    // Confirmed with the developer (2026-07-16): under this file's Review policy, PlanSourcesAsync's
    // changed-field check has no "empty-existing-side is not a real conflict" special case (that
    // logic exists in FieldMergeResolver for MergeOurs/MergeTheirs and decide-time auto-resolution,
    // but not for the Review "should this even go Pending" gate) — so a first-time null-to-value
    // SeriesId fill stages Pending exactly like a genuine disagreement does. This is accepted as-is
    // for #180, matching the plan doc's original "a human decides" intent, at the cost of a fresh
    // install staging one Pending action per Source the overlay touches (see the real
    // data/sources/quotinator-series-universe.json's ~75 entries) until each is decided and applied.

    /// <summary>A Source with no existing SeriesId still stages a Pending action under this file's Review policy — the review gate has no first-time-fill exception, so nothing is silently applied.</summary>
    [TestMethod]
    public async Task SeedSeriesUniverseOverlay_NoExistingSeriesId_StagesPendingUnderReviewPolicy()
    {
        string sourceId = Quotinator.Core.Import.EntityIdentity.SourceId("Test Movie", "Movie");
        string quotesFile = Path.Combine(_tempDir, "quotes.json");
        File.WriteAllText(quotesFile, """[{"id":"11111111-1111-1111-1111-111111111111","quote":"Hello there.","source":"Test Movie","type":"movie"}]""");
        string overlayFile = Path.Combine(_tempDir, "overlay.json");
        File.WriteAllText(overlayFile, $$"""
            {
              "quotes": [],
              "sources": [{"id":"{{sourceId}}","title":"Test Movie","type":"movie","seriesName":"Test Series"}],
              "series": [{"name":"Test Series"}]
            }
            """);

        SeedBatch batch = new SeedBatch(
            [new SeedFile(quotesFile, null), new SeedFile(overlayFile, null, Policy: new ManifestPolicy(DuplicateResolutionPolicy.Review))],
            ManifestPolicy.HardcodedDefault, "overlay-test");
        QuotinatorDatabaseInitializer db = CreateInitializer([batch]);
        await db.InitialiseAsync();

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);

        int pendingCount = (await new ImportActionReader(new SqliteConnectionFactory(_dbPath)).GetPagedAsync(null, "Pending", "Source", 1, 0)).TotalCount;
        Assert.AreEqual(1, pendingCount, "Review policy stages Pending for any changed field, including a first-time null-to-value SeriesId fill");

        string? seriesId = await conn.ExecuteScalarAsync<string?>(
            "SELECT SeriesId FROM Quotinator_Source WHERE Id = @id;", new { id = sourceId });
        Assert.IsNull(seriesId, "Nothing applied yet — SeriesId stays null until the Pending action is decided and applied");
    }

    /// <summary>Re-seeding the exact same overlay content a second time is a true no-op — SeriesId already matches, so nothing is staged at all.</summary>
    [TestMethod]
    public async Task SeedSeriesUniverseOverlay_AlreadyTagged_NoActionStaged()
    {
        string sourceId = Quotinator.Core.Import.EntityIdentity.SourceId("Test Movie", "Movie");
        string quotesFile = Path.Combine(_tempDir, "quotes.json");
        File.WriteAllText(quotesFile, """[{"id":"11111111-1111-1111-1111-111111111111","quote":"Hello there.","source":"Test Movie","type":"movie"}]""");
        string overlayFile = Path.Combine(_tempDir, "overlay.json");
        File.WriteAllText(overlayFile, $$"""
            {
              "quotes": [],
              "sources": [{"id":"{{sourceId}}","title":"Test Movie","type":"movie","seriesName":"Test Series"}],
              "series": [{"name":"Test Series"}]
            }
            """);

        SeedBatch batch = new SeedBatch(
            [new SeedFile(quotesFile, null), new SeedFile(overlayFile, null, Policy: new ManifestPolicy(DuplicateResolutionPolicy.NewestWins))],
            ManifestPolicy.HardcodedDefault, "overlay-test-seed");
        QuotinatorDatabaseInitializer db = CreateInitializer([batch]);
        await db.InitialiseAsync();

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);
        string? seriesId = await conn.ExecuteScalarAsync<string?>("SELECT SeriesId FROM Quotinator_Source WHERE Id = @id;", new { id = sourceId });
        Assert.IsNotNull(seriesId, "Sanity check — NewestWins applies immediately, so SeriesId must already be set before the second pass");

        Guid reapplyBatchId = Guid.NewGuid();
        using SqliteConnection reapplyConn = new SqliteConnection($"Data Source={_dbPath}");
        await reapplyConn.OpenAsync(TestContext.CancellationToken);
        IReadOnlyList<ImportActionEntity> actions = await Quotinator.Core.Database.ImportActionPlanner.PlanAsync(
            (SqliteConnection)reapplyConn, [], reapplyBatchId, DuplicateResolutionPolicy.Review,
            sources: [new Quotinator.Core.Import.SourceEntryDto { Id = sourceId, Title = "Test Movie", Type = Quotinator.Core.Enums.QuoteType.Movie, SeriesName = "Test Series" }]);

        Assert.AreEqual(0, actions.Count(a => a.EntityType == "Source"), "Identical content — no change, no action staged at all");
    }

    // -------------------------------------------------------------------------
    #region #277: backup real-work gating and storage pre-flight check

    // Restoring a WAL-mode backup (RestoreBackup reopens the backup file to read it) can leave
    // transient -shm/-wal sidecar files alongside the real backup — count only the .db files
    // themselves, one per actual CreateBackup call.
    private int BackupFileCount() => Directory.Exists(_backups) ? Directory.GetFiles(_backups, "*.db").Length : 0;

    private SeedBatch SimpleQuoteBatch()
    {
        string quotesFile = Path.Combine(_tempDir, $"quotes-{Guid.NewGuid():N}.json");
        File.WriteAllText(quotesFile,
            $$"""[{"id":"{{Guid.NewGuid()}}","quote":"Hello there.","source":"Test Movie","type":"movie","genres":["drama"]}]""");
        return new SeedBatch([new SeedFile(quotesFile, null)], ManifestPolicy.HardcodedDefault, "simple-test-seed");
    }

    private sealed class FakeDiskSpaceProvider(long availableBytes) : IDiskSpaceProvider
    {
        public long GetAvailableFreeSpaceBytes(string path) => availableBytes;
    }

    private sealed class ThrowingAuditEntryWriter : IAuditEntryWriter
    {
        public Task WriteAsync(AuditEntryEntity entry, IDbConnection connection, IDbTransaction? transaction = null)
            => throw new InvalidOperationException("simulated execute-step failure");
        public Task WriteAsync(IReadOnlyList<AuditEntryEntity> entries, IDbConnection connection, IDbTransaction? transaction = null) => Task.CompletedTask;
        public Task WriteAsync(AuditEntryEntity entry) => Task.CompletedTask;
        public Task ClearAsync(string? table = null) => Task.CompletedTask;
    }

    [TestMethod]
    public async Task InitialiseAsync_AlreadySeeded_TakesNoBackup()
    {
        SeedBatch batch = SimpleQuoteBatch();
        QuotinatorDatabaseInitializer db1 = CreateInitializer([batch], useBaseline: true);
        await db1.InitialiseAsync();

        int before = BackupFileCount();

        QuotinatorDatabaseInitializer db2 = CreateInitializer([batch], useBaseline: true);
        await db2.InitialiseAsync();

        Assert.AreEqual(before, BackupFileCount(), "A restart against an already-seeded database must take no backup");
    }

    [TestMethod]
    public async Task InitialiseAsync_ContentSeedNeeded_TakesBackup()
    {
        QuotinatorDatabaseInitializer db1 = CreateInitializer([], useBaseline: true);
        await db1.InitialiseAsync();
        Assert.AreEqual(0, BackupFileCount(), "Sanity check — the baseline path itself never backs up");

        QuotinatorDatabaseInitializer db2 = CreateInitializer([], useBaseline: true);
        await db2.InitialiseAsync();

        Assert.AreEqual(1, BackupFileCount(), "A database still needing content-seed work must take a backup");
    }

    [TestMethod]
    public async Task InitialiseAsync_AfterReset_ContentSeedNeeded_TakesBackup()
    {
        SeedBatch batch = SimpleQuoteBatch();
        QuotinatorDatabaseInitializer db = CreateInitializer([batch], useBaseline: true);
        await db.InitialiseAsync();
        Assert.AreEqual(0, BackupFileCount(), "Sanity check — the baseline path itself never backs up");

        await db.ResetAsync();
        Assert.IsNull(db.MigrationApplied, "Reset sets schema-version counters directly via the baseline path — MigrationApplied stays null even though content-seed has real work to do next");
        int afterReset = BackupFileCount();
        Assert.AreEqual(1, afterReset, "Reset's own backup must still fire");

        await db.InitialiseAsync();

        Assert.AreEqual(afterReset + 1, BackupFileCount(), "The startup immediately after a Reset must still take a backup — this is the exact case a MigrationApplied-based gate was found to miss");
    }

    [TestMethod]
    public async Task InitialiseAsync_MigrationPending_TakesBackup()
    {
        // Truncating QuotinatorMigrations.All would drop its own last entry (the domain-prefix
        // rename to Quotinator_Quote), breaking every later query in this test — instead, append a
        // harmless extra migration so db2 sees a genuinely pending migration on top of an otherwise
        // fully-migrated, correctly-named database.
        QuotinatorDatabaseInitializer db1 = CreateInitializer([], QuotinatorMigrations.All, useBaseline: true);
        await db1.InitialiseAsync();

        SchemaMigration extraMigration = new SchemaMigration
        {
            Version = QuotinatorMigrations.All.Count + 1,
            Sql     = "CREATE TABLE IF NOT EXISTS Test_277_Dummy (Id INTEGER);",
        };
        List<SchemaMigration> extendedMigrations = [.. QuotinatorMigrations.All, extraMigration];

        int before = BackupFileCount();
        QuotinatorDatabaseInitializer db2 = CreateInitializer([], extendedMigrations, useBaseline: true);
        await db2.InitialiseAsync();

        Assert.IsGreaterThan(before, BackupFileCount(), "A database with a pending migration must take a backup");
    }

    [TestMethod]
    public async Task CreateBackup_InsufficientStorageSpace_RefusesToSeedRatherThanProceedUnprotected()
    {
        QuotinatorDatabaseInitializer db1 = CreateInitializer([], useBaseline: true);
        await db1.InitialiseAsync();

        SeedBatch batch = SimpleQuoteBatch();
        QuotinatorDatabaseInitializer db2 = CreateInitializer([batch], useBaseline: true, diskSpaceProvider: new FakeDiskSpaceProvider(0));
        DatabaseOperationResult result = await db2.InitialiseAsync();

        Assert.IsFalse(result.Succeeded, "seeding must not proceed when no backup could be taken");
        Assert.AreEqual(BackupOutcome.InsufficientDiskSpace, result.BackupObstacle);
        Assert.AreEqual(0, BackupFileCount(), "Backup must be skipped, not written, when real free space is insufficient");

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);
        int quoteCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_Quote WHERE IsDeleted = 0;");
        Assert.AreEqual(0, quoteCount, "the database is left untouched rather than half-seeded with no restore point");
    }

    [TestMethod]
    public async Task CreateBackup_SufficientStorageSpace_ProceedsNormally()
    {
        QuotinatorDatabaseInitializer db1 = CreateInitializer([], useBaseline: true);
        await db1.InitialiseAsync();

        SeedBatch batch = SimpleQuoteBatch();
        QuotinatorDatabaseInitializer db2 = CreateInitializer([batch], useBaseline: true, diskSpaceProvider: NoOpDiskSpaceProvider.Instance);
        await db2.InitialiseAsync();

        Assert.AreEqual(1, BackupFileCount(), "Backup must be written when both budget and real free space are sufficient");
    }

    [TestMethod]
    public async Task InitialiseAsync_BackupWriteFails_ReportsTheObstacleRatherThanThrowing()
    {
        QuotinatorDatabaseInitializer db1 = CreateInitializer([], useBaseline: true);
        await db1.InitialiseAsync();

        // Blocks Directory.CreateDirectory(_backups) inside CreateBackup — a file already exists
        // at that exact path, so creating it as a directory throws IOException.
        File.WriteAllText(_backups, "blocker");

        SeedBatch batch = SimpleQuoteBatch();
        QuotinatorDatabaseInitializer db2 = CreateInitializer([batch], useBaseline: true);
        DatabaseOperationResult result = await db2.InitialiseAsync();

        // #348 replaced the DatabaseBackupWriteException this used to assert. The destination being
        // unwritable is detected, not unforeseen, so it is reported rather than thrown — and it is
        // reported as its own variant, distinguishable from a budget ceiling or an unreadable source,
        // which the single exception type could not express.
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(BackupOutcome.DestinationDirectoryNotWritable, result.BackupObstacle);
    }

    [TestMethod]
    public async Task InitialiseAsync_ExecuteStepFails_SurfacesDistinctFailureReason()
    {
        QuotinatorDatabaseInitializer db1 = CreateInitializer([], useBaseline: true);
        await db1.InitialiseAsync();
        Assert.AreEqual(0, BackupFileCount(), "Sanity check — the baseline path itself never backs up");

        SeedBatch batch = SimpleQuoteBatch();
        QuotinatorDatabaseInitializer db2 = CreateInitializer([batch], useBaseline: true, auditWriter: new ThrowingAuditEntryWriter());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => db2.InitialiseAsync());

        Assert.AreEqual(1, BackupFileCount(), "The backup must have succeeded before the execute step failed — distinguishing this from a backup-write failure");
    }

    #endregion

    public TestContext TestContext { get; set; }
}

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Import;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Engine.Database;
using Quotinator.Engine.Entities;
using Quotinator.Engine.Repositories;
using Quotinator.Engine.Services;

namespace Quotinator.Engine.Tests.Database;

/// <summary>
/// Covers Verification rows 8, 13, 14, and 15 of the #140 plan doc
/// (docs/milestones/data-import-sources/140-auto-update-sources-plan.md) — proving
/// <see cref="QuotinatorDatabaseInitializer"/> actually calls <see cref="ISourceCacheUpdater"/>
/// at the right moments with the right parameters, rather than relying on the fixed <c>_batches</c>
/// field it was built from at DI-construction time.
/// </summary>
[TestClass]
public class SourceCacheWiringTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static readonly string SourcesDir = Path.Combine(RepoRoot, "data", "sources");

    private static string CuratedFile => Path.Combine(SourcesDir, "quotinator-curated.json");

    private string _tempDir = null!;
    private string _dbPath  = null!;
    private string _backups = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_sourcecachewiring_").FullName;
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

    private QuotinatorDatabaseInitializer CreateInitializer(SpySourceCacheUpdater spy, bool autoUpdateSources)
    {
        var factory       = new SqliteConnectionFactory(_dbPath);
        var options       = new DatabaseOptions { DbPath = _dbPath, BackupsPath = _backups };
        var importBatches = new SqliteImportBatchRepository(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);
        var logger        = NullLogger<DatabaseInitializer>.Instance;
        var batch         = new SeedBatch([new SeedFile(CuratedFile, null)], ManifestPolicy.HardcodedDefault, "bundled sources");
        var actionReader  = new SystemImportActionReader(factory);
        var actionWriter  = new SystemImportActionWriter(factory);
        var coordinator   = new ImportActionResolutionCoordinator(actionReader, actionWriter, factory);
        var actionService = new SqliteImportActionService(actionReader, coordinator, NoOpSystemChangeLogWriter.Instance,
            new SqliteRestorableRepository<QuoteEntity>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Source>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Character>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Person>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            importBatches, factory);

        return new QuotinatorDatabaseInitializer(factory, options, QuotinatorMigrations.All, [batch], importBatches,
            coordinator, actionService,
            NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance, logger,
            spy, autoUpdateSources, QuotinatorMigrations.Baseline);
    }

    // Row 13: a second POST /reseed call re-evaluates staleness independently of the first — proves
    // the fixed-batch-list problem (SeedBatchesBuilder.Build() runs once at DI-construction time) no
    // longer prevents the updater from being consulted again on subsequent calls.
    [TestMethod]
    public async Task ReseedAsync_CalledTwice_InvokesUpdaterIndependentlyEachTime()
    {
        var spy = new SpySourceCacheUpdater();
        var db  = CreateInitializer(spy, autoUpdateSources: true);
        await db.InitialiseAsync();
        spy.Calls.Clear();

        await db.ReseedAsync();
        await db.ReseedAsync();

        Assert.AreEqual(2, spy.Calls.Count, "Each reseed call must independently re-resolve the effective batch list");
    }

    // Row 14: Reset triggers the same refresh-check logic as Reseed, including forceSourceRefresh threading.
    [TestMethod]
    public async Task ResetAsync_ForceSourceRefreshTrue_ThreadsThroughToUpdaterSameAsReseed()
    {
        var spy = new SpySourceCacheUpdater();
        var db  = CreateInitializer(spy, autoUpdateSources: true);
        await db.InitialiseAsync();
        spy.Calls.Clear();

        await db.ResetAsync(preserveSchemaVersion: false, forceSourceRefresh: true);

        Assert.AreEqual(1, spy.Calls.Count);
        Assert.IsTrue(spy.Calls[0].AllowNetwork);
        Assert.IsTrue(spy.Calls[0].ForceRefresh, "Reset must thread forceSourceRefresh through to the updater, same as Reseed");
    }

    [TestMethod]
    public async Task ReseedAsync_ForceSourceRefreshTrue_ThreadsThroughToUpdater()
    {
        var spy = new SpySourceCacheUpdater();
        var db  = CreateInitializer(spy, autoUpdateSources: true);
        await db.InitialiseAsync();
        spy.Calls.Clear();

        await db.ReseedAsync(forceSourceRefresh: true);

        Assert.AreEqual(1, spy.Calls.Count);
        Assert.IsTrue(spy.Calls[0].ForceRefresh);
    }

    // Row 15: PreviewSeedAsync reflects the cache but never triggers a network call, even when
    // Quotinator__AutoUpdateSources is true.
    [TestMethod]
    public async Task PreviewSeedAsync_AutoUpdateSourcesTrue_NeverAllowsNetwork()
    {
        var spy = new SpySourceCacheUpdater();
        var db  = CreateInitializer(spy, autoUpdateSources: true);
        await db.InitialiseAsync();
        spy.Calls.Clear();

        await db.PreviewSeedAsync();

        Assert.AreEqual(1, spy.Calls.Count);
        Assert.IsFalse(spy.Calls[0].AllowNetwork, "Preview must never trigger a network call regardless of AutoUpdateSources");
        Assert.IsFalse(spy.Calls[0].ForceRefresh);
    }

    // A malformed source file must be distinguishable, via the preview response, from a genuinely
    // empty-but-valid one — both would otherwise show quoteCount: 0 with no way to tell them apart.
    [TestMethod]
    public async Task PreviewSeedAsync_MalformedFile_ReportsInvalidJsonIssue()
    {
        var malformedPath = Path.Combine(_tempDir, "malformed.json");
        File.WriteAllText(malformedPath, "{ this is not valid json");

        var factory       = new SqliteConnectionFactory(_dbPath);
        var options       = new DatabaseOptions { DbPath = _dbPath, BackupsPath = _backups };
        var importBatches = new SqliteImportBatchRepository(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);
        var batch = new SeedBatch(
            [new SeedFile(CuratedFile, null), new SeedFile(malformedPath, null)],
            ManifestPolicy.HardcodedDefault, "bundled sources");
        var actionReader  = new SystemImportActionReader(factory);
        var actionWriter  = new SystemImportActionWriter(factory);
        var coordinator   = new ImportActionResolutionCoordinator(actionReader, actionWriter, factory);
        var actionService = new SqliteImportActionService(actionReader, coordinator, NoOpSystemChangeLogWriter.Instance,
            new SqliteRestorableRepository<QuoteEntity>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Source>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Character>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Person>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            importBatches, factory);
        var db = new QuotinatorDatabaseInitializer(factory, options, QuotinatorMigrations.All, [batch], importBatches,
            coordinator, actionService,
            NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance, NullLogger<DatabaseInitializer>.Instance,
            new SpySourceCacheUpdater(), autoUpdateSources: false, QuotinatorMigrations.Baseline);
        await db.InitialiseAsync();

        var preview = await db.PreviewSeedAsync();

        Assert.IsNull(preview.Files.Single(f => f.FileName == "quotinator-curated.json").Issue);
        Assert.AreEqual(SeedFileIssue.InvalidJson, preview.Files.Single(f => f.FileName == "malformed.json").Issue);
    }

    [TestMethod]
    public async Task PreviewSeedAsync_MissingFile_ReportsMissingIssue()
    {
        var missingPath = Path.Combine(_tempDir, "does-not-exist.json");

        var factory       = new SqliteConnectionFactory(_dbPath);
        var options       = new DatabaseOptions { DbPath = _dbPath, BackupsPath = _backups };
        var importBatches = new SqliteImportBatchRepository(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);
        var batch = new SeedBatch([new SeedFile(missingPath, null)], ManifestPolicy.HardcodedDefault, "bundled sources");
        var actionReader2  = new SystemImportActionReader(factory);
        var actionWriter2  = new SystemImportActionWriter(factory);
        var coordinator2   = new ImportActionResolutionCoordinator(actionReader2, actionWriter2, factory);
        var actionService2 = new SqliteImportActionService(actionReader2, coordinator2, NoOpSystemChangeLogWriter.Instance,
            new SqliteRestorableRepository<QuoteEntity>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Source>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Character>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Person>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            importBatches, factory);
        var db = new QuotinatorDatabaseInitializer(factory, options, QuotinatorMigrations.All, [batch], importBatches,
            coordinator2, actionService2,
            NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance, NullLogger<DatabaseInitializer>.Instance,
            new SpySourceCacheUpdater(), autoUpdateSources: false, QuotinatorMigrations.Baseline);
        await db.InitialiseAsync();

        var preview = await db.PreviewSeedAsync();

        Assert.AreEqual(SeedFileIssue.Missing, preview.Files.Single(f => f.FileName == "does-not-exist.json").Issue);
    }

    // Row 8: POST /api/v1/admin/sources/refresh (IDatabaseInitializer.RefreshSourcesAsync) updates
    // caches without touching the database — row counts must be unaffected by the call.
    [TestMethod]
    public async Task RefreshSourcesAsync_DoesNotAffectRowCountsOrTouchDatabase()
    {
        var spy = new SpySourceCacheUpdater();
        var db  = CreateInitializer(spy, autoUpdateSources: true);
        await db.InitialiseAsync();
        var quoteCountBefore = db.QuoteCount;
        spy.Calls.Clear();

        var resolution = await db.RefreshSourcesAsync(force: true);

        Assert.AreEqual(1, spy.Calls.Count);
        Assert.IsTrue(spy.Calls[0].AllowNetwork);
        Assert.IsTrue(spy.Calls[0].ForceRefresh);
        Assert.AreEqual(quoteCountBefore, db.QuoteCount, "RefreshSourcesAsync must never reimport or otherwise touch quote data");
        Assert.IsNotNull(resolution);
    }

    // PreviewSeedAsync must surface each file's refresh outcome/last-refreshed timestamp from the
    // updater's own resolution — not just the raw quote count — so an operator can tell a source
    // that's currently falling back due to a validation failure apart from one that's genuinely empty.
    [TestMethod]
    public async Task PreviewSeedAsync_AttachesRefreshOutcomeAndTimestampFromResolution()
    {
        var lastRefreshedAtUtc = DateTime.UtcNow.AddHours(-3);
        var spy = new SpySourceCacheUpdater
        {
            ResultsToReturn = [new SourceRefreshResult("quotinator-curated.json", "https://example.com/x", SourceRefreshOutcome.Failed, "boom", lastRefreshedAtUtc)]
        };
        var db = CreateInitializer(spy, autoUpdateSources: true);
        await db.InitialiseAsync();

        var preview = await db.PreviewSeedAsync();

        var filePreview = preview.Files.Single(f => f.FileName == "quotinator-curated.json");
        Assert.AreEqual(SourceRefreshOutcome.Failed, filePreview.RefreshOutcome);
        Assert.AreEqual(lastRefreshedAtUtc, filePreview.LastRefreshedAtUtc);
    }

    private sealed class SpySourceCacheUpdater : ISourceCacheUpdater
    {
        public List<(bool AllowNetwork, bool ForceRefresh)> Calls { get; } = [];

        public IReadOnlyList<SourceRefreshResult> ResultsToReturn { get; set; } = [];

        public Task<SourceCacheResolution> ResolveAsync(
            IReadOnlyList<SeedBatch> candidateBatches,
            bool allowNetwork,
            bool forceRefresh,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((allowNetwork, forceRefresh));
            return Task.FromResult(new SourceCacheResolution(candidateBatches, ResultsToReturn));
        }
    }
}

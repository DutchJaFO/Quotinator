using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Import;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Engine.Database;
using Quotinator.Engine.Repositories;

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

        return new QuotinatorDatabaseInitializer(factory, options, QuotinatorMigrations.All, [batch], importBatches,
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

    private sealed class SpySourceCacheUpdater : ISourceCacheUpdater
    {
        public List<(bool AllowNetwork, bool ForceRefresh)> Calls { get; } = [];

        public Task<SourceCacheResolution> ResolveAsync(
            IReadOnlyList<SeedBatch> candidateBatches,
            bool allowNetwork,
            bool forceRefresh,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((allowNetwork, forceRefresh));
            return Task.FromResult(new SourceCacheResolution(candidateBatches, []));
        }
    }
}

using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Core.Data;
using Quotinator.Core.Data.Repositories;
using Quotinator.Data.Connections;

namespace Quotinator.Core.Tests.Data;

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
        // Release pooled SQLite connections before deleting the temp DB file.
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private DatabaseInitializer CreateInitializer(IReadOnlyList<SeedBatch> batches)
    {
        var factory       = new SqliteConnectionFactory(_dbPath);
        var importBatches = new SqliteImportBatchRepository(factory);
        var logger        = NullLogger<DatabaseInitializer>.Instance;
        return new DatabaseInitializer(factory, _dbPath, _backups, batches, importBatches, logger);
    }

    private static SeedBatch AllFilesBatch() => new(
        [CuratedFile, VilaboimFile, NikhilNamal17File],
        ManifestPolicy.HardcodedDefault,
        "bundled sources");

    // ── Seeding ───────────────────────────────────────────────────────────────

    /// <summary>Seeding all three bundled source files produces the expected quote/source/character counts.</summary>
    [TestMethod]
    public async Task InitialiseAsync_AllSourceFiles_SeedsExpectedCounts()
    {
        var db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        Assert.AreEqual(788, db.QuoteCount,     "Unique quotes");
        Assert.AreEqual(478, db.SourceCount,    "Sources");
        Assert.AreEqual(2,   db.CharacterCount, "Characters");
        Assert.AreEqual(0,   db.PeopleCount,    "People");
    }

    /// <summary>Cross-file duplicates between vilaboim and NikhilNamal17 are recorded with Skip policy.</summary>
    [TestMethod]
    public async Task InitialiseAsync_AllSourceFiles_TracksCrossFileDuplicates()
    {
        var db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        Assert.AreEqual(45, db.LastSeedDuplicates.Count, "Cross-file duplicates");
        Assert.IsTrue(
            db.LastSeedDuplicates.All(d => d.AppliedPolicy == DuplicateResolutionPolicy.Skip),
            "All duplicates should use Skip policy (manifest default)");
    }

    /// <summary>Seeding only the curated file correctly wires up the FK chain: Source → Character → Quote.</summary>
    [TestMethod]
    public async Task InitialiseAsync_CuratedFileOnly_SeedsFkChainCorrectly()
    {
        var batch = new SeedBatch([CuratedFile], ManifestPolicy.HardcodedDefault, "curated");
        var db    = CreateInitializer([batch]);
        await db.InitialiseAsync();

        Assert.AreEqual(2, db.QuoteCount,     "2 curated quotes");
        Assert.AreEqual(1, db.SourceCount,    "1 source (Airplane!)");
        Assert.AreEqual(2, db.CharacterCount, "2 characters (Ted Striker, Dr. Rumack)");
        Assert.AreEqual(0, db.LastSeedDuplicates.Count);
    }

    /// <summary>No source files configured — database is created but stays empty.</summary>
    [TestMethod]
    public async Task InitialiseAsync_EmptyBatches_DatabaseIsEmpty()
    {
        var db = CreateInitializer([]);
        await db.InitialiseAsync();

        Assert.AreEqual(0, db.QuoteCount);
    }

    /// <summary>Calling InitialiseAsync a second time on an already-seeded database is a no-op.</summary>
    [TestMethod]
    public async Task InitialiseAsync_CalledTwice_IsIdempotent()
    {
        var db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        var countAfterFirst = db.QuoteCount;
        await db.InitialiseAsync();

        Assert.AreEqual(countAfterFirst, db.QuoteCount);
    }

    // ── Reset ─────────────────────────────────────────────────────────────────

    /// <summary>ResetAsync on an already-seeded database drops and recreates all tables and reseeds correctly.</summary>
    [TestMethod]
    public async Task ResetAsync_AfterInitialise_RebuildsSchemaAndReseeds()
    {
        var db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        var countAfterInit = db.QuoteCount;

        await db.ResetAsync();

        Assert.AreEqual(countAfterInit, db.QuoteCount, "Quote count after reset should match initial seed");
    }

    // ── Regression ────────────────────────────────────────────────────────────

    /// <summary>
    /// Regression test for issue #106: if SchemaVersion is rolled back to v2 while the
    /// underlying tables already have v3 columns (ImportBatchId), InitialiseAsync must
    /// self-heal — record the version and reseed — rather than crash-looping on every startup.
    /// This state was produced by the broken ResetAsync in v1.5.x–v1.6.1.
    /// </summary>
    [TestMethod]
    public async Task InitialiseAsync_PartialMigrationState_SelfHealsAndReseeds()
    {
        // Arrange: seed a healthy v3 database then simulate the broken state by rolling
        // SchemaVersion back to v2 — exactly what the pre-fix ResetAsync left behind.
        var db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        var countAfterInit = db.QuoteCount;

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await conn.ExecuteAsync("DELETE FROM SchemaVersion WHERE Version = 3;");
        await conn.CloseAsync();

        // Act: InitialiseAsync must not throw. It detects the duplicate-column situation,
        // records version 3, and leaves existing data intact.
        await db.InitialiseAsync();

        // Assert: schema is at v3 and existing data is undisturbed.
        Assert.AreEqual(3,              db.SchemaVersion, "Schema must be recorded at v3 after self-heal");
        Assert.AreEqual(countAfterInit, db.QuoteCount,    "Quote count must be unchanged after self-heal");
    }
}

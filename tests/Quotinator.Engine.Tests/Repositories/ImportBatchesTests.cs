using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Import;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Engine.Database;
using Quotinator.Engine.Entities;
using Quotinator.Engine.Services;

namespace Quotinator.Engine.Tests.Repositories;

[TestClass]
public class ImportBatchesTests
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
        _tempDir = Directory.CreateTempSubdirectory("quotinator_ibtest_").FullName;
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

    private QuotinatorDatabaseInitializer CreateInitializer(IReadOnlyList<SeedBatch> batches, bool useBaseline = true)
    {
        var factory       = new SqliteConnectionFactory(_dbPath);
        var options       = new DatabaseOptions { DbPath = _dbPath, BackupsPath = _backups };
        var importBatches = new SqliteImportBatchRepository(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);
        var logger        = NullLogger<DatabaseInitializer>.Instance;
        var actionReader  = new SystemImportActionReader(factory);
        var actionWriter  = new SystemImportActionWriter(factory);
        var coordinator   = new ImportActionResolutionCoordinator(actionReader, actionWriter, factory);
        var actionService = new SqliteImportActionService(actionReader, coordinator, NoOpSystemChangeLogWriter.Instance,
            new SqliteRestorableRepository<QuoteEntity>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Source>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Character>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Person>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<ConversationEntity>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<StageDirectionEntity>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<SoundCueEntity>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            importBatches, factory);
        return new QuotinatorDatabaseInitializer(factory, options, QuotinatorMigrations.All, batches, importBatches,
            coordinator, actionService,
            NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance, logger,
            NoOpSourceCacheUpdater.Instance, autoUpdateSources: false,
            useBaseline ? QuotinatorMigrations.Baseline : null);
    }

    // Simulates a pre-existing database at App (consumer) migration v2 — Sources/Quotes/etc.
    // created, genres reseeded, but ImportBatches (App migration 3) not yet applied. Writes
    // directly to System_ConsumerSchemaVersion (never had a legacy name — it's new in #143) rather
    // than the legacy "SchemaVersion" name, since these two rows represent App's own migration
    // history specifically, not Quotinator.Data's.
    private async Task CreateV2DatabaseAsync()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        await conn.ExecuteAsync("CREATE TABLE System_ConsumerSchemaVersion (Version INTEGER NOT NULL, AppliedAt TEXT NOT NULL)");
        await conn.ExecuteAsync("INSERT INTO System_ConsumerSchemaVersion VALUES (1, '2025-01-01 00:00:00')");
        await conn.ExecuteAsync("INSERT INTO System_ConsumerSchemaVersion VALUES (2, '2025-01-01 00:00:00')");
        await conn.ExecuteAsync("CREATE TABLE Quotes (Id TEXT PRIMARY KEY, QuoteText TEXT NOT NULL, IsDeleted INTEGER NOT NULL DEFAULT 0)");
        await conn.ExecuteAsync("CREATE TABLE Sources (Id TEXT PRIMARY KEY, IsDeleted INTEGER NOT NULL DEFAULT 0)");
        // #179's Migration009 reads Characters.SourceId/DateCreated (before dropping the column) and
        // Characters.Name/DateModified/DateDeleted/IsDeleted (rebuilding the table) — this stub must
        // carry the same base columns Migration001 actually created, or migration replay from this
        // simulated v2 state fails with "no such column" once it reaches Migration009.
        await conn.ExecuteAsync("CREATE TABLE Characters (Id TEXT PRIMARY KEY, SourceId TEXT, Name TEXT NOT NULL DEFAULT '', DateCreated TEXT NOT NULL DEFAULT '2025-01-01 00:00:00', DateModified TEXT, DateDeleted TEXT, IsDeleted INTEGER NOT NULL DEFAULT 0)");
        await conn.ExecuteAsync("CREATE TABLE People (Id TEXT PRIMARY KEY, IsDeleted INTEGER NOT NULL DEFAULT 0)");
        await conn.ExecuteAsync("CREATE TABLE QuoteGenres (Id TEXT PRIMARY KEY, QuoteId TEXT NOT NULL, Genre TEXT NOT NULL)");
        await conn.ExecuteAsync("INSERT INTO Quotes (Id, QuoteText) VALUES ('TEST-QUOTE-ID', 'Existing test quote')");
    }

    // ── Schema ────────────────────────────────────────────────────────────────

    /// <summary><c>ImportBatches</c> table is created with all required columns.</summary>
    [TestMethod]
    public async Task Schema_ImportBatchesTable_HasAllRequiredColumns()
    {
        var db = CreateInitializer([]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var columns = (await conn.QueryAsync<string>(
            "SELECT name FROM pragma_table_info('ImportBatches')")).ToHashSet();

        var expected = new[] { "Id", "Name", "Type", "Url", "ImportedAt", "ImportedBy", "RecordCount",
                                "DateCreated", "DateModified", "DateDeleted", "IsDeleted", "ConflictPolicy" };
        foreach (var col in expected)
            Assert.IsTrue(columns.Contains(col), $"Column '{col}' missing from ImportBatches");
    }

    /// <summary>The batch's actual applied conflict-resolution policy (for quotes) is persisted, not just backfilled for pre-existing rows.</summary>
    [TestMethod]
    public async Task Schema_ImportBatchesConflictPolicy_PersistsAppliedPolicy()
    {
        var batch = new SeedBatch([new SeedFile(CuratedFile, null)], new ManifestPolicy(DuplicateResolutionPolicy.MergeTheirs), "test");
        var db    = CreateInitializer([batch]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var conflictPolicy = await conn.ExecuteScalarAsync<string>(
            "SELECT ConflictPolicy FROM ImportBatches WHERE Name = @name", new { name = Path.GetFileName(CuratedFile) });

        Assert.AreEqual(nameof(DuplicateResolutionPolicy.MergeTheirs), conflictPolicy);
    }

    /// <summary>Nullable <c>ImportBatchId</c> FK column is present on all four entity tables.</summary>
    [TestMethod]
    public async Task Schema_EntityTables_HaveNullableImportBatchIdFK()
    {
        var db = CreateInitializer([]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();

        foreach (var table in new[] { "Quotes", "Sources", "Characters", "People" })
        {
            var col = await conn.QuerySingleOrDefaultAsync<(string name, int notNull)>(
                $"SELECT name, [notnull] FROM pragma_table_info('{table}') WHERE name = 'ImportBatchId'");
            Assert.IsNotNull(col.name, $"ImportBatchId missing from {table}");
            Assert.AreEqual(0, col.notNull, $"ImportBatchId on {table} must be nullable");
        }
    }

    /// <summary>App schema migration version is bumped to 9 after <c>InitialiseAsync</c>.</summary>
    [TestMethod]
    public async Task Schema_MigrationVersion_IsBumped()
    {
        var db = CreateInitializer([]);
        await db.InitialiseAsync();

        Assert.AreEqual(9, db.SchemaVersion, "SchemaVersion should be 9 after Migration009");
    }

    // ── Seeding ───────────────────────────────────────────────────────────────

    /// <summary>Seeder creates one <c>ImportBatch</c> row per source file; all bundled files get <c>Seed</c> type regardless of whether they declare a URL — the <c>Url</c> column itself carries the externally-sourced-vs-internally-authored distinction.</summary>
    [TestMethod]
    public async Task Seeding_TwoSourceFiles_ProduceTwoDistinctBatchesWithCorrectTypes()
    {
        var curatedFile = new SeedFile(CuratedFile, null);
        var seedFile    = new SeedFile(VilaboimFile, "https://github.com/vilaboim/movie-quotes");
        var batch       = new SeedBatch([curatedFile, seedFile], ManifestPolicy.HardcodedDefault, "test");
        var db          = CreateInitializer([batch]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var rows = (await conn.QueryAsync<(string Name, string Type, string? Url)>(
            "SELECT Name, Type, Url FROM ImportBatches WHERE IsDeleted = 0")).ToList();

        Assert.AreEqual(2, rows.Count, "One ImportBatch row per source file");
        Assert.AreEqual(rows.Count, rows.DistinctBy(r => r.Name).Count(), "All batch names are distinct");

        var curatedRow = rows.Single(r => r.Name == Path.GetFileName(CuratedFile));
        Assert.AreEqual("Seed", curatedRow.Type, "A bundled file without a URL is still Seed content, just internally-authored");
        Assert.IsNull(curatedRow.Url, "File without URL should have Url=NULL");

        var seedRow = rows.Single(r => r.Name == Path.GetFileName(VilaboimFile));
        Assert.AreEqual("Seed", seedRow.Type, "File with URL should have Type=Seed");
        Assert.AreEqual("https://github.com/vilaboim/movie-quotes", seedRow.Url, "Url should match the manifest URL");
    }

    /// <summary>Every Quotes row created during seeding is linked, via <c>ImportBatchId</c>, to the batch for the file it came from — not to some other batch or left <c>NULL</c>. Closes #57 Problem 4.</summary>
    [TestMethod]
    public async Task Seeding_TwoSourceFiles_QuotesLinkToOwningBatchAndRecordCountMatches()
    {
        var curatedFile = new SeedFile(CuratedFile, null);
        var seedFile    = new SeedFile(VilaboimFile, "https://github.com/vilaboim/movie-quotes");
        var batch       = new SeedBatch([curatedFile, seedFile], ManifestPolicy.HardcodedDefault, "test");
        var db         = CreateInitializer([batch]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();

        var batches = (await conn.QueryAsync<(string Id, string Name, int RecordCount)>(
            "SELECT Id, Name, RecordCount FROM ImportBatches WHERE IsDeleted = 0")).ToList();
        var curatedBatch  = batches.Single(b => b.Name == Path.GetFileName(CuratedFile));
        var vilaboimBatch = batches.Single(b => b.Name == Path.GetFileName(VilaboimFile));

        var quoteBatchIds = (await conn.QueryAsync<string?>("SELECT ImportBatchId FROM Quotes")).ToList();

        Assert.IsTrue(quoteBatchIds.All(id => id is not null), "Every seeded quote must have a non-null ImportBatchId");
        Assert.IsTrue(quoteBatchIds.All(id => id == curatedBatch.Id || id == vilaboimBatch.Id),
            "Every seeded quote must be linked to one of the two batches created for this seed run — not a third/unrelated batch");

        var curatedQuoteCount  = quoteBatchIds.Count(id => id == curatedBatch.Id);
        var vilaboimQuoteCount = quoteBatchIds.Count(id => id == vilaboimBatch.Id);

        Assert.IsTrue(curatedQuoteCount  > 0, "Curated batch should own at least one quote");
        Assert.IsTrue(vilaboimQuoteCount > 0, "Vilaboim batch should own at least one quote");
        Assert.AreEqual(curatedBatch.RecordCount,  curatedQuoteCount,  "ImportBatches.RecordCount must match the actual number of Quotes rows linked to the curated batch");
        Assert.AreEqual(vilaboimBatch.RecordCount, vilaboimQuoteCount, "ImportBatches.RecordCount must match the actual number of Quotes rows linked to the vilaboim batch");
    }

    /// <summary>An empty or otherwise invalid-JSON source file is skipped with a warning rather than crashing startup.</summary>
    [TestMethod]
    public async Task Seeding_EmptyOrInvalidJsonSourceFile_IsSkippedWithoutCrashing()
    {
        var emptyFile = Path.Combine(_tempDir, "empty.json");
        File.WriteAllText(emptyFile, string.Empty);

        var curatedFile = new SeedFile(CuratedFile, null);
        var emptySeedFile = new SeedFile(emptyFile, null);
        var batch = new SeedBatch([curatedFile, emptySeedFile], ManifestPolicy.HardcodedDefault, "test");
        var db = CreateInitializer([batch]);

        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();

        var quoteCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotes");
        Assert.IsTrue(quoteCount > 0, "Quotes from the valid curated file should still be seeded");

        var emptyBatch = await conn.QuerySingleAsync<(string Id, int RecordCount)>(
            "SELECT Id, RecordCount FROM ImportBatches WHERE Name = @name", new { name = "empty.json" });
        Assert.AreEqual(0, emptyBatch.RecordCount, "The empty/invalid file's batch should record zero quotes, not crash");
    }

    /// <summary>A file scanned from the user imports folder (Origin=UserImports) with no URL gets Type=UserSeed, not Seed.</summary>
    [TestMethod]
    public async Task Seeding_UserImportsOriginNoUrl_TypeIsUserSeed()
    {
        var userFile = new SeedFile(CuratedFile, null);
        var batch    = new SeedBatch([userFile], ManifestPolicy.HardcodedDefault, "test", SeedBatchOrigin.UserImports);
        var db       = CreateInitializer([batch]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var type = await conn.ExecuteScalarAsync<string>(
            "SELECT Type FROM ImportBatches WHERE Name = @name", new { name = Path.GetFileName(CuratedFile) });

        Assert.AreEqual("UserSeed", type, "A file scanned from the user imports folder must be UserSeed regardless of URL absence");
    }

    /// <summary>A file scanned from the user imports folder (Origin=UserImports) that DOES declare a URL still gets Type=UserSeed, not Seed — origin wins over URL presence.</summary>
    [TestMethod]
    public async Task Seeding_UserImportsOriginWithUrl_TypeIsStillUserSeed()
    {
        var userFile = new SeedFile(VilaboimFile, "https://github.com/vilaboim/movie-quotes");
        var batch    = new SeedBatch([userFile], ManifestPolicy.HardcodedDefault, "test", SeedBatchOrigin.UserImports);
        var db       = CreateInitializer([batch]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var type = await conn.ExecuteScalarAsync<string>(
            "SELECT Type FROM ImportBatches WHERE Name = @name", new { name = Path.GetFileName(VilaboimFile) });

        Assert.AreEqual("UserSeed", type, "A user-imports-folder file must stay UserSeed even when it declares its own URL — origin, not URL presence, decides the type");
    }

    // ── Migration (upgrade path) ───────────────────────────────────────────────

    /// <summary>ImportBatches.Type accepts 'UserSeed' without disturbing an existing 'Seed' row's Type.</summary>
    [TestMethod]
    public async Task ImportBatches_TypeCheckConstraint_AcceptsUserSeedAlongsideExistingSeedRow()
    {
        var seedBatch = new SeedBatch([new SeedFile(VilaboimFile, "https://github.com/vilaboim/movie-quotes")],
            ManifestPolicy.HardcodedDefault, "test", SeedBatchOrigin.Bundled);
        var db = CreateInitializer([seedBatch]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var existingRow = await conn.QuerySingleAsync<(string Id, string Type)>(
            "SELECT Id, Type FROM ImportBatches WHERE Name = @name", new { name = Path.GetFileName(VilaboimFile) });

        Assert.AreEqual("Seed", existingRow.Type, "Pre-existing row must retain its original Type");

        var newId = Guid.NewGuid().ToString();
        var now   = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO ImportBatches (Id, Name, Type, ImportedAt, RecordCount, DateCreated, IsDeleted) VALUES (@id, 'manual-user-seed.json', 'UserSeed', @now, 0, @now, 0)",
            new { id = newId, now });

        var insertedType = await conn.ExecuteScalarAsync<string>(
            "SELECT Type FROM ImportBatches WHERE Id = @id", new { id = newId });
        Assert.AreEqual("UserSeed", insertedType, "The widened CHECK constraint must accept 'UserSeed'");
    }

    /// <summary>Pre-seed batch rows for the two external datasets are inserted when upgrading a non-empty database.</summary>
    [TestMethod]
    public async Task Seeding_PreSeedBatches_ExistAfterMigration()
    {
        await CreateV2DatabaseAsync();

        var db = CreateInitializer([]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var seedBatches = (await conn.QueryAsync<string>(
            "SELECT Name FROM ImportBatches WHERE Type = 'Seed' AND IsDeleted = 0")).ToList();

        Assert.AreEqual(2, seedBatches.Count, "Two pre-seed batch rows expected after migration");
        Assert.IsTrue(seedBatches.Any(n => n.Contains("vilaboim")), "vilaboim batch row missing");
        Assert.IsTrue(seedBatches.Any(n => n.Contains("NikhilNamal17")), "NikhilNamal17 batch row missing");
    }

    /// <summary>Records that existed before Migration003 retain <c>NULL</c> <c>ImportBatchId</c> after the migration runs.</summary>
    [TestMethod]
    public async Task Migration_ExistingRecords_HaveNullImportBatchId()
    {
        await CreateV2DatabaseAsync();

        var db = CreateInitializer([]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var importBatchId = await conn.ExecuteScalarAsync<string?>(
            "SELECT ImportBatchId FROM Quotes WHERE Id = 'TEST-QUOTE-ID'");

        Assert.IsNull(importBatchId, "Pre-migration records must have NULL ImportBatchId");
    }
}

using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Import;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Engine.Database;
using Quotinator.Engine.Repositories;

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

    private QuotinatorDatabaseInitializer CreateInitializer(IReadOnlyList<SeedBatch> batches)
    {
        var factory       = new SqliteConnectionFactory(_dbPath);
        var options       = new DatabaseOptions { DbPath = _dbPath, BackupsPath = _backups };
        var importBatches = new SqliteImportBatchRepository(factory, NoOpAuditWriter.Instance, NoOpCallerContext.Instance);
        var logger        = NullLogger<DatabaseInitializer>.Instance;
        return new QuotinatorDatabaseInitializer(factory, options, QuotinatorMigrations.All, batches, importBatches,
            NoOpAuditWriter.Instance, NoOpCallerContext.Instance, logger);
    }

    private async Task CreateV2DatabaseAsync()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        await conn.ExecuteAsync("CREATE TABLE SchemaVersion (Version INTEGER NOT NULL, AppliedAt TEXT NOT NULL)");
        await conn.ExecuteAsync("INSERT INTO SchemaVersion VALUES (1, '2025-01-01 00:00:00')");
        await conn.ExecuteAsync("INSERT INTO SchemaVersion VALUES (2, '2025-01-01 00:00:00')");
        await conn.ExecuteAsync("CREATE TABLE Quotes (Id TEXT PRIMARY KEY, QuoteText TEXT NOT NULL, IsDeleted INTEGER NOT NULL DEFAULT 0)");
        await conn.ExecuteAsync("CREATE TABLE Sources (Id TEXT PRIMARY KEY, IsDeleted INTEGER NOT NULL DEFAULT 0)");
        await conn.ExecuteAsync("CREATE TABLE Characters (Id TEXT PRIMARY KEY, IsDeleted INTEGER NOT NULL DEFAULT 0)");
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
                                "DateCreated", "DateModified", "DateDeleted", "IsDeleted" };
        foreach (var col in expected)
            Assert.IsTrue(columns.Contains(col), $"Column '{col}' missing from ImportBatches");
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

    /// <summary>Schema migration version is bumped to 4 after <c>InitialiseAsync</c>.</summary>
    [TestMethod]
    public async Task Schema_MigrationVersion_IsBumped()
    {
        var db = CreateInitializer([]);
        await db.InitialiseAsync();

        Assert.AreEqual(4, db.SchemaVersion, "SchemaVersion should be 4 after Migration004");
    }

    // ── Seeding ───────────────────────────────────────────────────────────────

    /// <summary>Seeder creates one <c>ImportBatch</c> row per source file; files with a URL get <c>Seed</c> type, files without get <c>System</c>.</summary>
    [TestMethod]
    public async Task Seeding_TwoSourceFiles_ProduceTwoDistinctBatchesWithCorrectTypes()
    {
        var systemFile = new SeedFile(CuratedFile, null);
        var seedFile   = new SeedFile(VilaboimFile, "https://github.com/vilaboim/movie-quotes");
        var batch      = new SeedBatch([systemFile, seedFile], ManifestPolicy.HardcodedDefault, "test");
        var db         = CreateInitializer([batch]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var rows = (await conn.QueryAsync<(string Name, string Type, string? Url)>(
            "SELECT Name, Type, Url FROM ImportBatches WHERE IsDeleted = 0")).ToList();

        Assert.AreEqual(2, rows.Count, "One ImportBatch row per source file");
        Assert.AreEqual(rows.Count, rows.DistinctBy(r => r.Name).Count(), "All batch names are distinct");

        var systemRow = rows.Single(r => r.Name == Path.GetFileName(CuratedFile));
        Assert.AreEqual("System", systemRow.Type, "File without URL should have Type=System");
        Assert.IsNull(systemRow.Url, "File without URL should have Url=NULL");

        var seedRow = rows.Single(r => r.Name == Path.GetFileName(VilaboimFile));
        Assert.AreEqual("Seed", seedRow.Type, "File with URL should have Type=Seed");
        Assert.AreEqual("https://github.com/vilaboim/movie-quotes", seedRow.Url, "Url should match the manifest URL");
    }

    /// <summary>Every Quotes row created during seeding is linked, via <c>ImportBatchId</c>, to the batch for the file it came from — not to some other batch or left <c>NULL</c>. Closes #57 Problem 4.</summary>
    [TestMethod]
    public async Task Seeding_TwoSourceFiles_QuotesLinkToOwningBatchAndRecordCountMatches()
    {
        var systemFile = new SeedFile(CuratedFile, null);
        var seedFile   = new SeedFile(VilaboimFile, "https://github.com/vilaboim/movie-quotes");
        var batch      = new SeedBatch([systemFile, seedFile], ManifestPolicy.HardcodedDefault, "test");
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

    // ── Migration (upgrade path) ───────────────────────────────────────────────

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

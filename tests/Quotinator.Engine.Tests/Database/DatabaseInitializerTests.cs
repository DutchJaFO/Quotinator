using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Import;
using Quotinator.Data.Queries;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Engine.Database;
using Quotinator.Engine.Repositories;

namespace Quotinator.Engine.Tests.Database;

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

    private QuotinatorDatabaseInitializer CreateInitializer(IReadOnlyList<SeedBatch> batches)
    {
        var factory       = new SqliteConnectionFactory(_dbPath);
        var options       = new DatabaseOptions { DbPath = _dbPath, BackupsPath = _backups };
        var importBatches = new SqliteImportBatchRepository(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);
        var logger        = NullLogger<DatabaseInitializer>.Instance;
        return new QuotinatorDatabaseInitializer(factory, options, QuotinatorMigrations.All, batches, importBatches,
            NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance, logger);
    }

    private static SeedBatch AllFilesBatch() => new(
        [
            new SeedFile(CuratedFile,        null),
            new SeedFile(VilaboimFile,        "https://github.com/vilaboim/movie-quotes"),
            new SeedFile(NikhilNamal17File,   "https://github.com/NikhilNamal17/popular-movie-quotes")
        ],
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
        var batch = new SeedBatch([new SeedFile(CuratedFile, null)], ManifestPolicy.HardcodedDefault, "curated");
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

    // ── System table preservation (#141) ────────────────────────────────────────

    private const string MarkerValue = "manual-test-marker";

    /// <summary>A full Reset must not destroy the audit trail — System_AuditEntries is excluded from the table wipe.</summary>
    [TestMethod]
    public async Task ResetAsync_AfterInitialise_PreservesExistingAuditEntries()
    {
        var db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        await InsertAuditMarkerAsync();

        await db.ResetAsync();

        Assert.AreEqual(1, await CountAuditMarkerRowsAsync(), "Full Reset must preserve existing System_AuditEntries rows");
    }

    /// <summary>With the default parameter, Reset still clears and replays SchemaVersion — unchanged historical behaviour.</summary>
    [TestMethod]
    public async Task ResetAsync_DefaultParameter_StillReplaysSchemaVersion()
    {
        var db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        await InsertSchemaVersionMarkerAsync();

        await db.ResetAsync();

        Assert.AreEqual(0, await CountSchemaVersionMarkerRowsAsync(),
            "Default Reset should clear and replay SchemaVersion, removing the pre-existing marker row");
    }

    /// <summary>With preserveSchemaVersion:true, Reset leaves existing SchemaVersion rows untouched.</summary>
    [TestMethod]
    public async Task ResetAsync_PreserveSchemaVersionTrue_KeepsExistingRows()
    {
        var db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        await InsertSchemaVersionMarkerAsync();

        await db.ResetAsync(preserveSchemaVersion: true);

        Assert.AreEqual(1, await CountSchemaVersionMarkerRowsAsync(),
            "preserveSchemaVersion:true should leave existing SchemaVersion rows untouched");
    }

    /// <summary>Reseed (not Reset) has always left System_AuditEntries and System_SchemaVersion alone — this makes that behaviour explicit.</summary>
    [TestMethod]
    public async Task ReseedAsync_AfterInitialise_LeavesAuditEntriesAndSchemaVersionUntouched()
    {
        var db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        await InsertAuditMarkerAsync();
        await InsertSchemaVersionMarkerAsync();

        await db.ReseedAsync();

        Assert.AreEqual(1, await CountAuditMarkerRowsAsync(),        "Reseed must not touch System_AuditEntries");
        Assert.AreEqual(1, await CountSchemaVersionMarkerRowsAsync(), "Reseed must not touch System_SchemaVersion");
    }

    private async Task InsertAuditMarkerAsync()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "INSERT INTO System_AuditEntries (TableName, RecordId, Operation, Agent, PerformedAt) " +
            "VALUES ('Quotes', 'test-id', 'Insert', @marker, '2026-01-01 00:00:00');",
            new { marker = MarkerValue });
    }

    private async Task<int> CountAuditMarkerRowsAsync()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM System_AuditEntries WHERE Agent = @marker;", new { marker = MarkerValue });
    }

    private async Task InsertSchemaVersionMarkerAsync()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "INSERT INTO System_SchemaVersion (Version, AppliedAt) VALUES (1, @marker);", new { marker = MarkerValue });
    }

    private async Task<int> CountSchemaVersionMarkerRowsAsync()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM System_SchemaVersion WHERE AppliedAt = @marker;", new { marker = MarkerValue });
    }

    // ── System-prefix naming convention (#141 amendment) ───────────────────────

    /// <summary>
    /// GetUserTables excludes any table whose name literally starts with "System_", proving
    /// Quotinator.Data needs no knowledge of specific system table names — a consuming project
    /// can define its own protected table (here, System_FooBar) with zero changes to Sql.cs.
    /// </summary>
    [TestMethod]
    public async Task GetUserTables_SystemPrefixedTable_IsExcluded()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await conn.ExecuteAsync("CREATE TABLE System_FooBar (Id INTEGER);");
        await conn.ExecuteAsync("CREATE TABLE FooBar (Id INTEGER);");

        var tables = (await conn.QueryAsync<string>(Sql.Schema.GetUserTables)).ToList();

        Assert.IsFalse(tables.Contains("System_FooBar"), "System_-prefixed tables must be excluded");
        Assert.IsTrue(tables.Contains("FooBar"), "Non-prefixed tables must still be included");
    }

    /// <summary>
    /// A table that merely starts with "System" without the underscore (e.g. SystemInventory) is
    /// NOT treated as protected — proves the ESCAPE clause in GetUserTables is doing real work,
    /// since SQL LIKE treats '_' as a single-character wildcard and an unescaped 'System_%' would
    /// wrongly match this table too.
    /// </summary>
    [TestMethod]
    public async Task GetUserTables_SystemPrefixWithoutUnderscore_IsNotExcluded()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await conn.ExecuteAsync("CREATE TABLE SystemInventory (Id INTEGER);");

        var tables = (await conn.QueryAsync<string>(Sql.Schema.GetUserTables)).ToList();

        Assert.IsTrue(tables.Contains("SystemInventory"),
            "A table starting with 'System' but no underscore must NOT be treated as protected");
    }

    /// <summary>A fresh database creates System_SchemaVersion directly — it is never created under the legacy name and then renamed.</summary>
    [TestMethod]
    public async Task InitialiseAsync_FreshDatabase_CreatesSystemSchemaVersionDirectly()
    {
        var db = CreateInitializer([]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        var legacyCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'SchemaVersion';");
        var systemCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'System_SchemaVersion';");

        Assert.AreEqual(0, legacyCount, "A fresh database must never contain a table literally named SchemaVersion");
        Assert.AreEqual(1, systemCount, "A fresh database must create System_SchemaVersion directly");
    }

    /// <summary>
    /// Builds a fully up-to-date database, then downgrades it back to the pre-amendment table
    /// names (SchemaVersion, AuditEntries with the original IX_AuditEntries_* index names) and
    /// removes migration006's recorded version — simulating a real database that predates this
    /// amendment, without hand-rolling the full legacy schema by hand.
    /// </summary>
    private async Task DowngradeToLegacyNamesAsync()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await conn.ExecuteAsync("DELETE FROM System_SchemaVersion WHERE Version = 6;");
        await conn.ExecuteAsync("ALTER TABLE System_SchemaVersion RENAME TO SchemaVersion;");
        await conn.ExecuteAsync("ALTER TABLE System_AuditEntries RENAME TO AuditEntries;");
        await conn.ExecuteAsync("DROP INDEX IF EXISTS IX_System_AuditEntries_TableName_RecordId;");
        await conn.ExecuteAsync("DROP INDEX IF EXISTS IX_System_AuditEntries_PerformedAt;");
        await conn.ExecuteAsync("CREATE INDEX IX_AuditEntries_TableName_RecordId ON AuditEntries (TableName, RecordId);");
        await conn.ExecuteAsync("CREATE INDEX IX_AuditEntries_PerformedAt ON AuditEntries (PerformedAt);");
    }

    /// <summary>
    /// A database with a pre-existing legacy SchemaVersion table (simulating an upgrade from
    /// before this amendment) gets it renamed to System_SchemaVersion, with existing version
    /// history rows preserved.
    /// </summary>
    [TestMethod]
    public async Task InitialiseAsync_LegacySchemaVersionTable_IsRenamedWithRowsPreserved()
    {
        var db = CreateInitializer([]);
        await db.InitialiseAsync();
        await DowngradeToLegacyNamesAsync();

        var db2 = CreateInitializer([]);
        await db2.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        var legacyCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'SchemaVersion';");
        var preservedRows = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM System_SchemaVersion WHERE Version IN (1, 2, 3, 4, 5);");

        Assert.AreEqual(0, legacyCount, "The legacy SchemaVersion table must no longer exist after the rename");
        Assert.AreEqual(5, preservedRows, "The five pre-existing version history rows must survive the rename");
        Assert.AreEqual(6, db2.SchemaVersion, "Only migration006 should have replayed, bringing the schema back to v6");
    }

    /// <summary>Migration006 renames AuditEntries to System_AuditEntries and preserves existing rows and both indexes.</summary>
    [TestMethod]
    public async Task InitialiseAsync_LegacyAuditEntriesTable_MigratesToSystemAuditEntriesWithRowsPreserved()
    {
        var db = CreateInitializer([]);
        await db.InitialiseAsync();
        await DowngradeToLegacyNamesAsync();

        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync(
                "INSERT INTO AuditEntries (TableName, RecordId, Operation, Agent, PerformedAt) " +
                "VALUES ('Quotes', 'test-id', 'Insert', @marker, '2026-01-01 00:00:00');",
                new { marker = MarkerValue });
        }

        var db2 = CreateInitializer([]);
        await db2.InitialiseAsync();

        using var verifyConn = new SqliteConnection($"Data Source={_dbPath}");
        await verifyConn.OpenAsync();
        var legacyCount = await verifyConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'AuditEntries';");
        var preservedRow = await verifyConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM System_AuditEntries WHERE Agent = @marker;", new { marker = MarkerValue });
        var indexNames = (await verifyConn.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'System_AuditEntries';")).ToList();

        Assert.AreEqual(0, legacyCount, "The legacy AuditEntries table must no longer exist after migration006");
        Assert.AreEqual(1, preservedRow, "The pre-existing audit row must survive the rename");
        Assert.IsTrue(indexNames.Contains("IX_System_AuditEntries_TableName_RecordId"), "TableName/RecordId index must exist under the new name");
        Assert.IsTrue(indexNames.Contains("IX_System_AuditEntries_PerformedAt"), "PerformedAt index must exist under the new name");
    }

    // ── Regression ────────────────────────────────────────────────────────────

    /// <summary>
    /// Regression test for issue #106: if SchemaVersion is rolled back to v2 while the
    /// underlying tables already have v3 columns (ImportBatchId), InitialiseAsync must
    /// self-heal — record the version and reseed — rather than crash-looping on every startup.
    /// </summary>
    [TestMethod]
    public async Task InitialiseAsync_PartialMigrationState_SelfHealsAndReseeds()
    {
        var db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        var countAfterInit = db.QuoteCount;

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await conn.ExecuteAsync("DELETE FROM System_SchemaVersion WHERE Version IN (3, 4, 5, 6);");
        await conn.CloseAsync();

        await db.InitialiseAsync();

        Assert.AreEqual(6,              db.SchemaVersion, "Schema must be recorded at v6 after self-heal");
        Assert.AreEqual(countAfterInit, db.QuoteCount,    "Quote count must be unchanged after self-heal");
    }
}

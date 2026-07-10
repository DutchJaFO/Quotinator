using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Import;
using Quotinator.Data.Queries;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Engine.Database;
using Quotinator.Engine.Entities;
using Quotinator.Engine.Repositories;
using Quotinator.Engine.Services;

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

    private QuotinatorDatabaseInitializer CreateInitializer(IReadOnlyList<SeedBatch> batches, bool useBaseline = true)
        => CreateInitializer(batches, QuotinatorMigrations.All, useBaseline);

    private QuotinatorDatabaseInitializer CreateInitializer(IReadOnlyList<SeedBatch> batches, IReadOnlyList<SchemaMigration> migrations, bool useBaseline)
    {
        var factory       = new SqliteConnectionFactory(_dbPath);
        var options       = new DatabaseOptions { DbPath = _dbPath, BackupsPath = _backups };
        var importBatches = new SqliteImportBatchRepository(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);
        var logger        = NullLogger<DatabaseInitializer>.Instance;
        var actionReader   = new SystemImportActionReader(factory);
        var actionWriter   = new SystemImportActionWriter(factory);
        var coordinator    = new ImportActionResolutionCoordinator(actionReader, actionWriter, factory);
        var actionService  = new SqliteImportActionService(actionReader, coordinator, NoOpSystemChangeLogWriter.Instance,
            new SqliteRestorableRepository<QuoteEntity>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Source>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Character>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Person>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            importBatches, factory);
        return new QuotinatorDatabaseInitializer(factory, options, migrations, batches, importBatches,
            coordinator, actionService,
            NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance, logger,
            NoOpSourceCacheUpdater.Instance, autoUpdateSources: false,
            useBaseline ? QuotinatorMigrations.Baseline : null);
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
        Assert.AreEqual(479, db.SourceCount,    "Sources");
        Assert.AreEqual(2,   db.CharacterCount, "Characters");
        Assert.AreEqual(0,   db.PeopleCount,    "People");
    }

    /// <summary>Cross-file duplicates between vilaboim and NikhilNamal17 are recorded with NewestWins policy (AllFilesBatch() uses ManifestPolicy.HardcodedDefault directly, bypassing the bundled manifest.json's own "skip" override).</summary>
    [TestMethod]
    public async Task InitialiseAsync_AllSourceFiles_TracksCrossFileDuplicates()
    {
        var db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        Assert.AreEqual(45, db.LastSeedDuplicates.Count, "Cross-file duplicates");
        Assert.IsTrue(
            db.LastSeedDuplicates.All(d => d.AppliedPolicy == DuplicateResolutionPolicy.NewestWins),
            "All duplicates should use NewestWins policy (HardcodedDefault)");
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

    /// <summary>
    /// Quotinator.Data's own migrations concern only System_-prefixed tables (System_AuditEntries),
    /// which a Reset never drops — so System_SchemaVersion must never be wiped or replayed by a
    /// Reset, regardless of preserveSchemaVersion. This is stronger than "preserved": it's simply
    /// never touched.
    /// </summary>
    [TestMethod]
    public async Task ResetAsync_AnyParameter_NeverTouchesDataSchemaVersion()
    {
        var db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();
        await InsertSchemaVersionMarkerAsync();

        await db.ResetAsync(preserveSchemaVersion: false);
        Assert.AreEqual(1, await CountSchemaVersionMarkerRowsAsync(),
            "System_SchemaVersion must survive a default Reset — it was never wiped in the first place");

        await db.ResetAsync(preserveSchemaVersion: true);
        Assert.AreEqual(1, await CountSchemaVersionMarkerRowsAsync(),
            "System_SchemaVersion must survive a preserveSchemaVersion:true Reset too — same reason");
    }

    /// <summary>With the default parameter, Reset still clears and replays System_ConsumerSchemaVersion — unchanged historical behaviour for the consumer's own migrations.</summary>
    [TestMethod]
    public async Task ResetAsync_DefaultParameter_StillReplaysConsumerSchemaVersion()
    {
        var db = CreateInitializer([AllFilesBatch()]);
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
        var db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();

        await InsertConsumerSchemaVersionMarkerAsync();

        await db.ResetAsync(preserveSchemaVersion: true);

        Assert.AreEqual(1, await CountConsumerSchemaVersionMarkerRowsAsync(),
            "preserveSchemaVersion:true should leave existing System_ConsumerSchemaVersion rows untouched");
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
            "INSERT INTO System_AuditEntries (Id, TableName, RecordId, Operation, Agent, PerformedAt, DateCreated) " +
            "VALUES (lower(hex(randomblob(16))), 'Quotes', 'test-id', 'Insert', @marker, '2026-01-01 00:00:00', '2026-01-01 00:00:00');",
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

    private async Task InsertConsumerSchemaVersionMarkerAsync()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "INSERT INTO System_ConsumerSchemaVersion (Version, AppliedAt) VALUES (1, @marker);", new { marker = MarkerValue });
    }

    private async Task<int> CountConsumerSchemaVersionMarkerRowsAsync()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM System_ConsumerSchemaVersion WHERE AppliedAt = @marker;", new { marker = MarkerValue });
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
    /// Builds a fully up-to-date database, then downgrades it back to the pre-#141 table names
    /// (SchemaVersion, AuditEntries with the original IX_AuditEntries_* index names) and rolls
    /// Data's own version counter back to v1 (create-only, rename not yet applied) — simulating a
    /// real database that predates the #141 amendment, without hand-rolling the full legacy schema.
    /// </summary>
    private async Task DowngradeToLegacyNamesAsync()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await conn.ExecuteAsync("DELETE FROM System_SchemaVersion;");
        await conn.ExecuteAsync(
            "INSERT INTO System_SchemaVersion (Version, AppliedAt) VALUES (1, @marker);", new { marker = MarkerValue });
        await conn.ExecuteAsync("ALTER TABLE System_SchemaVersion RENAME TO SchemaVersion;");

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
            "SELECT TableName, RecordId, Operation, Agent, PerformedAt FROM System_AuditEntries;");
        await conn.ExecuteAsync("DROP TABLE System_AuditEntries;");
        await conn.ExecuteAsync("CREATE INDEX IX_AuditEntries_TableName_RecordId ON AuditEntries (TableName, RecordId);");
        await conn.ExecuteAsync("CREATE INDEX IX_AuditEntries_PerformedAt ON AuditEntries (PerformedAt);");
    }

    /// <summary>
    /// A database with a pre-existing legacy SchemaVersion table (simulating an upgrade from
    /// before the #141 amendment) gets it renamed to System_SchemaVersion, with the existing
    /// version-history row preserved rather than wiped.
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
        var preservedRow = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM System_SchemaVersion WHERE Version = 1 AND AppliedAt = @marker;", new { marker = MarkerValue });

        Assert.AreEqual(0, legacyCount, "The legacy SchemaVersion table must no longer exist after the rename");
        Assert.AreEqual(1, preservedRow, "The pre-existing version-history row must survive the rename, not be wiped");
        Assert.AreEqual(9, db2.DataSchemaVersion, "Data migrations 2-9 (the rename, System_ImportConflicts, System_ChangeLog, both RecordBase retrofits, ExistingBatchId, System_ImportActions, and the Status CHECK constraint) should all have replayed after the legacy rename");
    }

    /// <summary>Data migration 2 renames AuditEntries to System_AuditEntries and preserves existing rows and both indexes.</summary>
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

        Assert.AreEqual(0, legacyCount, "The legacy AuditEntries table must no longer exist after Data migration 2");
        Assert.AreEqual(1, preservedRow, "The pre-existing audit row must survive the rename");
        Assert.IsTrue(indexNames.Contains("IX_System_AuditEntries_TableName_RecordId"), "TableName/RecordId index must exist under the new name");
        Assert.IsTrue(indexNames.Contains("IX_System_AuditEntries_PerformedAt"), "PerformedAt index must exist under the new name");
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
    /// Deletes every version row from 3 upward, not just "the last two" — <c>GetConsumerCurrentVersion</c>
    /// computes <c>MAX(Version)</c>, not row count, so leaving migration 5's row in place (e.g. deleting
    /// only 3 and 4) would leave the computed version at 5 and InitialiseAsync would see nothing pending
    /// to replay, defeating the whole scenario. Deleting 3 upward drops MAX back to 2, reproducing the
    /// original #106 scenario regardless of how many migrations now exist above it.
    /// </remarks>
    [TestMethod]
    public async Task InitialiseAsync_PartialMigrationState_FailsSafelyAndRequiresExplicitReset()
    {
        var db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseForTestingAsync(forceIncremental: true);

        var countAfterInit = db.QuoteCount;

        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("DELETE FROM System_ConsumerSchemaVersion WHERE Version >= 3;");
        }

        var db2 = CreateInitializer([AllFilesBatch()]);
        await Assert.ThrowsExactlyAsync<SqliteException>(() => db2.InitialiseAsync());

        using (var verifyConn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await verifyConn.OpenAsync();
            var quoteCountAfterFailedAttempt = await verifyConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotes;");
            Assert.AreEqual(countAfterInit, quoteCountAfterFailedAttempt,
                "Database must be restored to its pre-attempt state after a failed migration, not left partially migrated");
        }

        var db3 = CreateInitializer([AllFilesBatch()]);
        await db3.ResetAsync();
        Assert.AreEqual(8, db3.SchemaVersion, "An explicit Reset must fully resolve the version/schema mismatch");
    }

    // ── #143 — migration ownership split + baseline schema ─────────────────────

    private (QuotinatorDatabaseInitializer Db, string DbPath) CreateForcedIncrementalInitializer()
    {
        var dbPath        = Path.Combine(_tempDir, $"test_incremental_{Guid.NewGuid():N}.db");
        var factory       = new SqliteConnectionFactory(dbPath);
        var options       = new DatabaseOptions { DbPath = dbPath, BackupsPath = _backups };
        var importBatches = new SqliteImportBatchRepository(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);
        var actionReader  = new SystemImportActionReader(factory);
        var actionWriter  = new SystemImportActionWriter(factory);
        var coordinator   = new ImportActionResolutionCoordinator(actionReader, actionWriter, factory);
        var actionService = new SqliteImportActionService(actionReader, coordinator, NoOpSystemChangeLogWriter.Instance,
            new SqliteRestorableRepository<QuoteEntity>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Source>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Character>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Person>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            importBatches, factory);
        var db = new QuotinatorDatabaseInitializer(factory, options, QuotinatorMigrations.All, [], importBatches,
            coordinator, actionService,
            NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance, NullLogger<DatabaseInitializer>.Instance,
            NoOpSourceCacheUpdater.Instance, autoUpdateSources: false,
            QuotinatorMigrations.Baseline);
        return (db, dbPath);
    }

    private static async Task<List<string>> DumpTableSchemaAsync(SqliteConnection conn, string table)
    {
        var lines = new List<string>();

        var columns = await conn.QueryAsync<(int cid, string name, string type, int notnull, string? dflt_value, int pk)>(
            $"SELECT cid, name, type, [notnull], dflt_value, pk FROM pragma_table_info('{table}');");
        foreach (var c in columns.OrderBy(c => c.cid))
            lines.Add($"COL {c.cid} {c.name} {c.type} notnull={c.notnull} default={c.dflt_value} pk={c.pk}");

        var indexes = await conn.QueryAsync<(string name, int unique)>(
            $"SELECT name, [unique] FROM pragma_index_list('{table}');");
        foreach (var idx in indexes.OrderBy(i => i.name))
        {
            var idxCols = await conn.QueryAsync<(int seqno, string? name)>(
                $"SELECT seqno, name FROM pragma_index_info('{idx.name}');");
            var colList = string.Join(",", idxCols.OrderBy(c => c.seqno).Select(c => c.name));
            lines.Add($"IDX {idx.name} unique={idx.unique} cols=({colList})");
        }

        return lines;
    }

    private static readonly string[] EngineDomainTables =
        ["ImportBatches", "Sources", "SourceTranslations", "Characters", "CharacterTranslations",
         "People", "Quotes", "QuoteTranslations", "QuoteGenres",
         "Conversations", "ConversationLines", "StageDirections", "StageDirectionTranslations",
         "SoundCues", "SoundCueTranslations"];

    /// <summary>
    /// QuotinatorMigrations.Baseline must produce the exact same schema, table by table, as
    /// replaying QuotinatorMigrations.All incrementally. Comparison uses PRAGMA table_info/
    /// index_list/index_info rather than raw sqlite_master text, since hand-formatted baseline SQL
    /// and migration-assembled SQL (e.g. Sources' ImportBatchId appended via ALTER TABLE) would
    /// differ textually even when semantically identical.
    /// </summary>
    [TestMethod]
    public async Task Baseline_And_IncrementalReplay_ProduceIdenticalEngineSchema()
    {
        var dbA = CreateInitializer([]);
        await dbA.InitialiseAsync();

        var (dbB, dbPathB) = CreateForcedIncrementalInitializer();
        await dbB.InitialiseForTestingAsync(forceIncremental: true);

        using var connA = new SqliteConnection($"Data Source={_dbPath}");
        await connA.OpenAsync();
        using var connB = new SqliteConnection($"Data Source={dbPathB}");
        await connB.OpenAsync();

        foreach (var table in EngineDomainTables)
        {
            var schemaA = await DumpTableSchemaAsync(connA, table);
            var schemaB = await DumpTableSchemaAsync(connB, table);
            CollectionAssert.AreEqual(schemaB, schemaA,
                $"Table '{table}' schema differs between the baseline and incremental paths — " +
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
        var dbA = CreateInitializer([]);
        await dbA.InitialiseAsync();

        var (dbB, dbPathB) = CreateForcedIncrementalInitializer();
        await dbB.InitialiseForTestingAsync(forceIncremental: true);

        using var connA = new SqliteConnection($"Data Source={_dbPath}");
        await connA.OpenAsync();
        using var connB = new SqliteConnection($"Data Source={dbPathB}");
        await connB.OpenAsync();

        foreach (var conn in new[] { connA, connB })
        {
            // QuoteGenres.QuoteId is a FK to Quotes(Id) — irrelevant to the CHECK constraint being
            // tested here, so disable enforcement rather than seed a matching Quotes row.
            await conn.ExecuteAsync("PRAGMA foreign_keys = OFF;");

            var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            await conn.ExecuteAsync(
                "INSERT INTO ImportBatches (Id, Name, Type, ImportedAt, RecordCount, DateCreated, IsDeleted) " +
                "VALUES (@id, 'check-test.json', 'UserSeed', @now, 0, @now, 0);",
                new { id = Guid.NewGuid().ToString(), now });

            await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
                "INSERT INTO ImportBatches (Id, Name, Type, ImportedAt, RecordCount, DateCreated, IsDeleted) " +
                "VALUES (@id, 'bad.json', 'NotARealType', @now, 0, @now, 0);",
                new { id = Guid.NewGuid().ToString(), now }));

            await conn.ExecuteAsync(
                "INSERT INTO ImportBatches (Id, Name, Type, ImportedAt, RecordCount, DateCreated, IsDeleted, Status) " +
                "VALUES (@id, 'check-test-staged.json', 'Import', @now, 0, @now, 0, 'Staged');",
                new { id = Guid.NewGuid().ToString(), now });

            await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
                "INSERT INTO ImportBatches (Id, Name, Type, ImportedAt, RecordCount, DateCreated, IsDeleted, Status) " +
                "VALUES (@id, 'bad-status.json', 'Import', @now, 0, @now, 0, 'NotARealStatus');",
                new { id = Guid.NewGuid().ToString(), now }));

            await conn.ExecuteAsync(
                "INSERT INTO Sources (Id, Title, Type, DateCreated, IsDeleted) VALUES (@id, 'CheckTest', 'Person', @now, 0);",
                new { id = Guid.NewGuid().ToString(), now });

            await conn.ExecuteAsync(
                "INSERT INTO QuoteGenres (Id, QuoteId, Genre, DateCreated, IsDeleted) " +
                "VALUES (@id, @quoteId, 'SciFi', @now, 0);",
                new { id = Guid.NewGuid().ToString(), quoteId = Guid.NewGuid().ToString(), now });

            // ConversationLines carries two independent CHECK constraints (#67): a simple
            // LineType-membership CHECK (ADR 008) and a separate cross-field CHECK enforcing that
            // exactly the FK matching LineType is populated. Both are exercised here.
            var quoteLineId = Guid.NewGuid().ToString();
            await conn.ExecuteAsync(
                "INSERT INTO ConversationLines (Id, ConversationId, [Order], LineType, QuoteId, DateCreated, IsDeleted) " +
                "VALUES (@id, @conversationId, 1, 'Quote', @quoteId, @now, 0);",
                new { id = quoteLineId, conversationId = Guid.NewGuid().ToString(), quoteId = Guid.NewGuid().ToString(), now });

            await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
                "INSERT INTO ConversationLines (Id, ConversationId, [Order], LineType, QuoteId, DateCreated, IsDeleted) " +
                "VALUES (@id, @conversationId, 2, 'NotARealLineType', @quoteId, @now, 0);",
                new { id = Guid.NewGuid().ToString(), conversationId = Guid.NewGuid().ToString(), quoteId = Guid.NewGuid().ToString(), now }));

            await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
                "INSERT INTO ConversationLines (Id, ConversationId, [Order], LineType, StageDirectionId, DateCreated, IsDeleted) " +
                "VALUES (@id, @conversationId, 3, 'Quote', @stageDirectionId, @now, 0);",
                new { id = Guid.NewGuid().ToString(), conversationId = Guid.NewGuid().ToString(), stageDirectionId = Guid.NewGuid().ToString(), now }));
        }
    }

    // ── #67 — Conversations schema ──────────────────────────────────────────────

    private static readonly string[] ConversationTablesWithRecordBase =
        ["Conversations", "ConversationLines", "StageDirections", "StageDirectionTranslations",
         "SoundCues", "SoundCueTranslations"];

    /// <summary>Every table added by #67 carries RecordBase's four audit columns — ADR 002 applies without exception, including the line/junction table and both translation tables.</summary>
    [TestMethod]
    public async Task ConversationTables_AllHaveRecordBaseColumns()
    {
        var db = CreateInitializer([]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        foreach (var table in ConversationTablesWithRecordBase)
        {
            var columns = (await conn.QueryAsync<string>(
                $"SELECT name FROM pragma_table_info('{table}');")).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var recordBaseColumn in new[] { "Id", "DateCreated", "DateModified", "DateDeleted", "IsDeleted" })
                Assert.IsTrue(columns.Contains(recordBaseColumn), $"{table} is missing RecordBase column {recordBaseColumn}");
        }
    }

    /// <summary><c>UNIQUE (ConversationId, Order)</c> rejects a second line at an already-used position.</summary>
    [TestMethod]
    public async Task ConversationLines_UniqueConstraint_RejectsDuplicateOrder()
    {
        var db = CreateInitializer([]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await conn.ExecuteAsync("PRAGMA foreign_keys = OFF;");

        var now            = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var conversationId = Guid.NewGuid().ToString();

        await conn.ExecuteAsync(
            "INSERT INTO ConversationLines (Id, ConversationId, [Order], LineType, QuoteId, DateCreated, IsDeleted) " +
            "VALUES (@id, @conversationId, 1, 'Quote', @quoteId, @now, 0);",
            new { id = Guid.NewGuid().ToString(), conversationId, quoteId = Guid.NewGuid().ToString(), now });

        await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
            "INSERT INTO ConversationLines (Id, ConversationId, [Order], LineType, QuoteId, DateCreated, IsDeleted) " +
            "VALUES (@id, @conversationId, 1, 'Quote', @quoteId, @now, 0);",
            new { id = Guid.NewGuid().ToString(), conversationId, quoteId = Guid.NewGuid().ToString(), now }));
    }

    /// <summary><c>UNIQUE (StageDirectionId, Language)</c> and <c>UNIQUE (SoundCueId, Language)</c> reject a second translation in the same language.</summary>
    [TestMethod]
    public async Task TranslationTables_UniqueConstraint_RejectsDuplicateLanguage()
    {
        var db = CreateInitializer([]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await conn.ExecuteAsync("PRAGMA foreign_keys = OFF;");

        var now              = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var stageDirectionId = Guid.NewGuid().ToString();
        var soundCueId       = Guid.NewGuid().ToString();

        await conn.ExecuteAsync(
            "INSERT INTO StageDirectionTranslations (Id, StageDirectionId, Language, Text, DateCreated, IsDeleted) " +
            "VALUES (@id, @stageDirectionId, 'nl', 'Tekst', @now, 0);",
            new { id = Guid.NewGuid().ToString(), stageDirectionId, now });

        await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
            "INSERT INTO StageDirectionTranslations (Id, StageDirectionId, Language, Text, DateCreated, IsDeleted) " +
            "VALUES (@id, @stageDirectionId, 'nl', 'Andere tekst', @now, 0);",
            new { id = Guid.NewGuid().ToString(), stageDirectionId, now }));

        await conn.ExecuteAsync(
            "INSERT INTO SoundCueTranslations (Id, SoundCueId, Language, Text, DateCreated, IsDeleted) " +
            "VALUES (@id, @soundCueId, 'nl', 'Tekst', @now, 0);",
            new { id = Guid.NewGuid().ToString(), soundCueId, now });

        await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
            "INSERT INTO SoundCueTranslations (Id, SoundCueId, Language, Text, DateCreated, IsDeleted) " +
            "VALUES (@id, @soundCueId, 'nl', 'Andere tekst', @now, 0);",
            new { id = Guid.NewGuid().ToString(), soundCueId, now }));
    }

    /// <summary><see cref="ConversationLineType"/> round-trips through Dapper as a real enum, not an int — the <see cref="Data.Helpers.SafeEnumHandler{TEnum}"/> pattern already used for <see cref="ImportBatchType"/>/<see cref="ImportBatchStatus"/>.</summary>
    [TestMethod]
    public async Task ConversationLineType_RoundTripsThroughDapper()
    {
        var db = CreateInitializer([]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await conn.ExecuteAsync("PRAGMA foreign_keys = OFF;");

        var lineId = Guid.NewGuid();
        await conn.ExecuteAsync(
            "INSERT INTO ConversationLines (Id, ConversationId, [Order], LineType, StageDirectionId, DateCreated, IsDeleted) " +
            "VALUES (@id, @conversationId, 1, 'StageDirection', @stageDirectionId, @now, 0);",
            new
            {
                id             = lineId.ToString(),
                conversationId = Guid.NewGuid().ToString(),
                stageDirectionId = Guid.NewGuid().ToString(),
                now            = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            });

        var line = await conn.QuerySingleAsync<ConversationLineEntity>(
            "SELECT * FROM ConversationLines WHERE Id = @id;", new { id = lineId.ToString() });

        Assert.AreEqual(ConversationLineType.StageDirection, line.LineType.Parsed);
    }

    /// <summary>A fresh (zero-table) database takes the baseline path — both version tables end up with exactly one row each, at the final version.</summary>
    [TestMethod]
    public async Task InitialiseAsync_TrulyEmptyDatabase_TakesBaselinePathNotIncremental()
    {
        var db = CreateInitializer([]);
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        var dataRows     = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM System_SchemaVersion;");
        var consumerRows = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM System_ConsumerSchemaVersion;");

        Assert.AreEqual(1, dataRows,     "Baseline path should insert exactly one row into System_SchemaVersion");
        Assert.AreEqual(1, consumerRows, "Baseline path should insert exactly one row into System_ConsumerSchemaVersion");
        Assert.AreEqual(9, db.DataSchemaVersion);
        Assert.AreEqual(8, db.SchemaVersion);
    }

    /// <summary>
    /// An existing database with only App migrations pending still replays incrementally — the
    /// baseline path and the two migration phases never cross.
    /// </summary>
    /// <remarks>
    /// Builds the initial database with only migrations 1-3 actually applied (rather than applying
    /// all migrations and then deleting version rows) — migration 4 rebuilds the ImportBatches table
    /// from scratch, which would silently discard migration 5/6's ADD COLUMN effects if they were
    /// physically present, masking a genuine version/schema mismatch instead of exercising a real
    /// version-3 replay. Migrations 6+ (e.g. #55's IsComplete/NoValueKnown) ALTER tables that are
    /// never rebuilt, so replaying them a second time on top of already-applied columns would throw
    /// "duplicate column name" — a real bug in the old delete-then-replay technique, not a bug in
    /// the migrations themselves.
    /// </remarks>
    [TestMethod]
    public async Task InitialiseAsync_ExistingDatabaseAtVersion3_StillReplaysRemainingConsumerMigrationsIncrementally()
    {
        var partialMigrations = QuotinatorMigrations.All.Take(3).ToList();
        var db = CreateInitializer([], partialMigrations, useBaseline: false);
        await db.InitialiseForTestingAsync(forceIncremental: true);

        var db2 = CreateInitializer([]);
        await db2.InitialiseAsync();

        Assert.AreEqual(8, db2.SchemaVersion,     "All five remaining App migrations (4, 5, 6, 7, and 8) should have replayed");
        Assert.AreEqual(9, db2.DataSchemaVersion, "Data's own migrations were already fully applied and must not replay");
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
        var db = CreateInitializer([AllFilesBatch()]);
        await db.InitialiseAsync();
        var quoteCountBefore = db.QuoteCount;

        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("DROP TABLE System_ConsumerSchemaVersion;");
            await conn.ExecuteAsync("DELETE FROM System_SchemaVersion;");
            for (var v = 1; v <= 13; v++)
                await conn.ExecuteAsync(
                    "INSERT INTO System_SchemaVersion (Version, AppliedAt) VALUES (@v, @at);",
                    new { v, at = $"2026-01-01T00:00:{v:D2}Z" });
        }

        var db2 = CreateInitializer([AllFilesBatch()]);
        await Assert.ThrowsExactlyAsync<SqliteException>(() => db2.InitialiseAsync());

        using (var verifyConn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await verifyConn.OpenAsync();
            var quoteCountAfterFailedAttempt = await verifyConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotes;");
            Assert.AreEqual(quoteCountBefore, quoteCountAfterFailedAttempt,
                "Database must be restored to its pre-attempt state after the failed startup, not left partially migrated");
        }

        var db3 = CreateInitializer([AllFilesBatch()]);
        await db3.ResetAsync();
        Assert.AreEqual(8, db3.SchemaVersion, "An explicit Reset must fully resolve the mismatch");
    }

}

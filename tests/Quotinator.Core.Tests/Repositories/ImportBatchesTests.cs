using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Enums;
using Quotinator.Data.Import;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Core.Database;
using Quotinator.Core.Entities;
using Quotinator.Core.Services;

namespace Quotinator.Core.Tests.Repositories;

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
        => CreateInitializer(batches, QuotinatorMigrations.All, useBaseline);

    private QuotinatorDatabaseInitializer CreateInitializer(IReadOnlyList<SeedBatch> batches, IReadOnlyList<SchemaMigration> migrations, bool useBaseline)
    {
        var factory       = new SqliteConnectionFactory(_dbPath);
        var options       = new DatabaseOptions { DbPath = _dbPath, BackupsPath = _backups };
        var importBatches = new SqliteImportBatchRepository(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance);
        var logger        = NullLogger<DatabaseInitializer>.Instance;
        var actionReader  = new ImportActionReader(factory);
        var actionWriter  = new ImportActionWriter(factory);
        var coordinator   = new ImportActionResolutionCoordinator(actionReader, actionWriter, factory);
        var actionService = new SqliteImportActionService(actionReader, coordinator, actionWriter, NoOpAuditEntryWriter.Instance, NoOpChangeWriter.Instance,
            new SqliteRestorableRepository<QuoteEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<SourceEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<CharacterEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<PersonEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<ConversationEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<StageDirectionEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<SoundCueEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            importBatches, factory);
        return new QuotinatorDatabaseInitializer(factory, options, migrations, batches, importBatches,
            coordinator, actionService, actionWriter,
            NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance, logger,
            NoOpSourceCacheUpdater.Instance, autoUpdateSources: false,
            autoPurgeBundledImportActions: false, autoPurgeUserImportActions: false,
            NoOpRuleFileOverridePathResolver.Instance, NoOpSourceFileOverrideRegistry.Instance,
            NoOpFileResourceRepository.Instance,
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
        // This fixture simulates the state BEFORE migration 6 (the domain-prefix rename) runs, so
        // every stub below is deliberately created under its pre-#254 name (Sources/Characters/
        // People/Quotes/QuoteGenres) — migration 6 is what rebuilds these into their final
        // Quotinator_-prefixed names (and repoints ImportBatchId at Import_Batch instead of
        // ImportBatches), so replaying it from here is exactly what these tests exercise. Migration 5
        // (#150's CHECK constraint, frozen/already-shipped — never edit its content, only add new
        // migrations after it) also replays for real from this v2 state, but it only touches
        // ImportBatches.ConflictPolicy and needs no stub tables of its own. Every one of these stubs
        // must carry the same base columns Migration001 actually created, or migration replay from
        // this simulated v2 state fails with "no such column" once it reaches migration 6.
        await conn.ExecuteAsync("CREATE TABLE Sources (Id TEXT PRIMARY KEY, Title TEXT NOT NULL DEFAULT '', Type TEXT NOT NULL DEFAULT 'Movie', Date TEXT, DateCreated TEXT NOT NULL DEFAULT '2025-01-01 00:00:00', DateModified TEXT, DateDeleted TEXT, IsDeleted INTEGER NOT NULL DEFAULT 0)");
        // Migration006's rebuild pass touches every one of Migration001's original tables, not just
        // Sources/Characters/People/Quotes/QuoteGenres — SourceTranslations/CharacterTranslations/
        // QuoteTranslations must exist too (even empty) or its "INSERT ... SELECT ... FROM
        // SourceTranslations" step fails with "no such table" once replay reaches migration 6.
        await conn.ExecuteAsync("CREATE TABLE SourceTranslations (Id TEXT PRIMARY KEY, SourceId TEXT NOT NULL, Language TEXT NOT NULL, Title TEXT NOT NULL, DateCreated TEXT NOT NULL DEFAULT '2025-01-01 00:00:00', DateModified TEXT, DateDeleted TEXT, IsDeleted INTEGER NOT NULL DEFAULT 0)");
        // #179's Migration009 reads Characters.SourceId/DateCreated (before dropping the column) and
        // Characters.Name/DateModified/DateDeleted/IsDeleted (rebuilding the table) — this stub must
        // carry the same base columns Migration001 actually created, or migration replay from this
        // simulated v2 state fails with "no such column" once it reaches Migration009.
        await conn.ExecuteAsync("CREATE TABLE Characters (Id TEXT PRIMARY KEY, SourceId TEXT, Name TEXT NOT NULL DEFAULT '', DateCreated TEXT NOT NULL DEFAULT '2025-01-01 00:00:00', DateModified TEXT, DateDeleted TEXT, IsDeleted INTEGER NOT NULL DEFAULT 0)");
        await conn.ExecuteAsync("CREATE TABLE CharacterTranslations (Id TEXT PRIMARY KEY, CharacterId TEXT NOT NULL, Language TEXT NOT NULL, Name TEXT NOT NULL, DateCreated TEXT NOT NULL DEFAULT '2025-01-01 00:00:00', DateModified TEXT, DateDeleted TEXT, IsDeleted INTEGER NOT NULL DEFAULT 0)");
        await conn.ExecuteAsync("CREATE TABLE People (Id TEXT PRIMARY KEY, Name TEXT NOT NULL DEFAULT '', DateOfBirth TEXT, DateOfDeath TEXT, DateCreated TEXT NOT NULL DEFAULT '2025-01-01 00:00:00', DateModified TEXT, DateDeleted TEXT, IsDeleted INTEGER NOT NULL DEFAULT 0)");
        await conn.ExecuteAsync("CREATE TABLE Quotes (Id TEXT PRIMARY KEY, QuoteText TEXT NOT NULL, OriginalLanguage TEXT NOT NULL DEFAULT 'en', SourceId TEXT NOT NULL REFERENCES Sources(Id), CharacterId TEXT, PersonId TEXT, DateCreated TEXT NOT NULL DEFAULT '2025-01-01 00:00:00', DateModified TEXT, DateDeleted TEXT, IsDeleted INTEGER NOT NULL DEFAULT 0)");
        await conn.ExecuteAsync("CREATE TABLE QuoteTranslations (Id TEXT PRIMARY KEY, QuoteId TEXT NOT NULL, Language TEXT NOT NULL, QuoteText TEXT NOT NULL, DateCreated TEXT NOT NULL DEFAULT '2025-01-01 00:00:00', DateModified TEXT, DateDeleted TEXT, IsDeleted INTEGER NOT NULL DEFAULT 0)");
        await conn.ExecuteAsync("CREATE TABLE QuoteGenres (Id TEXT PRIMARY KEY, QuoteId TEXT NOT NULL, Genre TEXT NOT NULL, DateCreated TEXT NOT NULL DEFAULT '2025-01-01 00:00:00', DateModified TEXT, DateDeleted TEXT, IsDeleted INTEGER NOT NULL DEFAULT 0)");
        await conn.ExecuteAsync(
            "INSERT INTO Sources (Id, Title, Type, DateCreated) VALUES ('TEST-SOURCE-ID', 'Existing test source', 'Movie', '2025-01-01 00:00:00');");
        await conn.ExecuteAsync(
            "INSERT INTO Quotes (Id, QuoteText, SourceId) VALUES ('TEST-QUOTE-ID', 'Existing test quote', 'TEST-SOURCE-ID');");
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
            "SELECT name FROM pragma_table_info('Import_Batch')")).ToHashSet();

        var expected = new[] { "Id", "Name", "Type", "Url", "ImportedAt", "ImportedById", "RecordCount",
                                "DateCreated", "DateModified", "DateDeleted", "IsDeleted", "ConflictPolicy" };
        foreach (var col in expected)
            Assert.Contains(col, columns, $"Column '{col}' missing from Import_Batch");
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
            "SELECT ConflictPolicy FROM Import_Batch WHERE Name = @name", new { name = Path.GetFileName(CuratedFile) });

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

        foreach (var table in new[] { "Quotinator_Quote", "Quotinator_Source", "Quotinator_Character", "Quotinator_Person" })
        {
            var (name, notNull) = await conn.QuerySingleOrDefaultAsync<(string name, int notNull)>(
                $"SELECT name, [notnull] FROM pragma_table_info('{table}') WHERE name = 'ImportBatchId'");
            Assert.IsNotNull(name, $"ImportBatchId missing from {table}");
            Assert.AreEqual(0, notNull, $"ImportBatchId on {table} must be nullable");
        }
    }

    /// <summary>App schema migration version is bumped to 6 after <c>InitialiseAsync</c>.</summary>
    [TestMethod]
    public async Task Schema_MigrationVersion_IsBumped()
    {
        var db = CreateInitializer([]);
        await db.InitialiseAsync();

        Assert.AreEqual(6, db.SchemaVersion, "SchemaVersion should be 6: #155's consolidation of migrations 4-11 into one (4), #150's ImportBatches.ConflictPolicy CHECK constraint migration (5), plus #254's domain-prefix rename (6)");
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
            "SELECT Name, Type, Url FROM Import_Batch WHERE IsDeleted = 0")).ToList();

        Assert.HasCount(2, rows, "One ImportBatch row per source file");
        Assert.HasCount(rows.Count, rows.DistinctBy(r => r.Name), "All batch names are distinct");

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
            "SELECT Id, Name, RecordCount FROM Import_Batch WHERE IsDeleted = 0")).ToList();
        var (Id, Name, RecordCount) = batches.Single(b => b.Name == Path.GetFileName(CuratedFile));
        var vilaboimBatch = batches.Single(b => b.Name == Path.GetFileName(VilaboimFile));

        var quoteBatchIds = (await conn.QueryAsync<string?>("SELECT ImportBatchId FROM Quotinator_Quote")).ToList();

        Assert.IsTrue(quoteBatchIds.All(id => id is not null), "Every seeded quote must have a non-null ImportBatchId");
        Assert.IsTrue(quoteBatchIds.All(id => id == Id || id == vilaboimBatch.Id),
            "Every seeded quote must be linked to one of the two batches created for this seed run — not a third/unrelated batch");

        var curatedQuoteCount  = quoteBatchIds.Count(id => id == Id);
        var vilaboimQuoteCount = quoteBatchIds.Count(id => id == vilaboimBatch.Id);

        Assert.IsGreaterThan(0, curatedQuoteCount, "Curated batch should own at least one quote");
        Assert.IsGreaterThan(0, vilaboimQuoteCount, "Vilaboim batch should own at least one quote");
        Assert.AreEqual(RecordCount,  curatedQuoteCount,  "ImportBatches.RecordCount must match the actual number of Quotes rows linked to the curated batch");
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

        var quoteCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotinator_Quote");
        Assert.IsGreaterThan(0, quoteCount, "Quotes from the valid curated file should still be seeded");

        var (Id, RecordCount) = await conn.QuerySingleAsync<(string Id, int RecordCount)>(
            "SELECT Id, RecordCount FROM Import_Batch WHERE Name = @name", new { name = "empty.json" });
        Assert.AreEqual(0, RecordCount, "The empty/invalid file's batch should record zero quotes, not crash");
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
            "SELECT Type FROM Import_Batch WHERE Name = @name", new { name = Path.GetFileName(CuratedFile) });

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
            "SELECT Type FROM Import_Batch WHERE Name = @name", new { name = Path.GetFileName(VilaboimFile) });

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
            "SELECT Id, Type FROM Import_Batch WHERE Name = @name", new { name = Path.GetFileName(VilaboimFile) });

        Assert.AreEqual("Seed", existingRow.Type, "Pre-existing row must retain its original Type");

        var newId = Guid.NewGuid().ToString();
        var now   = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO Import_Batch (Id, Name, Type, ImportedAt, RecordCount, DateCreated, IsDeleted) VALUES (@id, 'manual-user-seed.json', 'UserSeed', @now, 0, @now, 0)",
            new { id = newId, now });

        var insertedType = await conn.ExecuteScalarAsync<string>(
            "SELECT Type FROM Import_Batch WHERE Id = @id", new { id = newId });
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
            "SELECT Name FROM Import_Batch WHERE Type = 'Seed' AND IsDeleted = 0")).ToList();

        Assert.HasCount(2, seedBatches, "Two pre-seed batch rows expected after migration");
        Assert.Contains(n => n.Contains("vilaboim"), seedBatches, "vilaboim batch row missing");
        Assert.Contains(n => n.Contains("NikhilNamal17"), seedBatches, "NikhilNamal17 batch row missing");
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
            "SELECT ImportBatchId FROM Quotinator_Quote WHERE Id = 'TEST-QUOTE-ID'");

        Assert.IsNull(importBatchId, "Pre-migration records must have NULL ImportBatchId");
    }

    /// <summary>
    /// #155: consolidated migration 4 (#213's original standalone migration 10, folded in) renames
    /// <c>ImportBatches.ImportedBy</c> to <c>ImportedById</c> via a single atomic
    /// <c>ALTER TABLE ... RENAME COLUMN</c> partway through, preserving any pre-existing value.
    /// <para/>
    /// Per #155: a test never truncates the migration list passed to a <c>DatabaseInitializer</c> —
    /// there is no code path where the real app "pretends" some migrations don't exist, so a test
    /// shouldn't either. Instead, a genuine v1.7.2-shaped fixture is built by executing the three
    /// Consumer migrations that actually shipped in that release (frozen forever, confirmed against
    /// `main`) directly against a raw connection, then recording that same true fact — migrations
    /// 1-3 really did just run — in <c>System_ConsumerSchemaVersion</c> (mirroring the existing
    /// <see cref="CreateV2DatabaseAsync"/> precedent in this file). The migration list itself stays
    /// the full, real, untruncated <see cref="QuotinatorMigrations.All"/> (still 4 entries) — this
    /// only tells the initializer what has already genuinely happened, so the run below is the real
    /// v1.7.2 → current upgrade path: only migration 4 replays, exactly as it would for a real user.
    /// </summary>
    [TestMethod]
    public async Task Migration_RenameImportedByToImportedById_ColumnRenamedAndDataPreserved()
    {
        var batchId = Guid.NewGuid().ToString();
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await conn.OpenAsync(TestContext.CancellationToken);
            await conn.ExecuteAsync(QuotinatorMigrations.Migration001_InitialSchema);
            await conn.ExecuteAsync(QuotinatorMigrations.Migration002_ReseedGenres);
            await conn.ExecuteAsync(QuotinatorMigrations.Migration003_ImportBatches);

            var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            await conn.ExecuteAsync(
                "INSERT INTO ImportBatches (Id, Name, Type, ImportedAt, ImportedBy, RecordCount, DateCreated, IsDeleted) " +
                "VALUES (@id, 'pre-rename.json', 'Import', @now, '22222222-2222-4222-8222-222222222222', 0, @now, 0);",
                new { id = batchId, now });

            await conn.ExecuteAsync("CREATE TABLE System_ConsumerSchemaVersion (Version INTEGER NOT NULL, AppliedAt TEXT NOT NULL)");
            await conn.ExecuteAsync(
                "INSERT INTO System_ConsumerSchemaVersion (Version, AppliedAt) VALUES (1, @now), (2, @now), (3, @now);",
                new { now });
        }

        var db = CreateInitializer([]);
        await db.InitialiseAsync();

        using var verifyConn = new SqliteConnection($"Data Source={_dbPath}");
        await verifyConn.OpenAsync(TestContext.CancellationToken);

        var columns = (await verifyConn.QueryAsync<string>(
            "SELECT name FROM pragma_table_info('Import_Batch')")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ImportedById", columns, "ImportedById column must exist after Migration010");
        Assert.DoesNotContain("ImportedBy", columns, "ImportedBy must no longer exist after the rename");

        var preservedValue = await verifyConn.ExecuteScalarAsync<string>(
            "SELECT ImportedById FROM Import_Batch WHERE Id = @id;", new { id = batchId });
        Assert.AreEqual("22222222-2222-4222-8222-222222222222", preservedValue,
            "The pre-existing value must survive the rename unchanged");
    }

    /// <summary>
    /// #150, ADR 008: migration 5 adds a CHECK constraint to <c>ImportBatches.ConflictPolicy</c>.
    /// Every row created via application code always stamps <c>DuplicateResolutionPolicy.ToString()</c>
    /// (PascalCase), but the column's original <c>ALTER TABLE ... ADD COLUMN ... DEFAULT 'skip'</c>
    /// backfill (migration 4) wrote that literal lowercase default directly into pre-existing rows,
    /// never through application code. This proves migration 5's copy step normalises exactly that
    /// legacy value to the PascalCase form the new CHECK requires, without losing the row.
    /// </summary>
    [TestMethod]
    public async Task Migration_ImportBatchConflictPolicyCheckConstraint_NormalisesLegacyLowercaseDefault()
    {
        var batchId = Guid.NewGuid().ToString();
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await conn.OpenAsync(TestContext.CancellationToken);
            await conn.ExecuteAsync(QuotinatorMigrations.Migration001_InitialSchema);
            await conn.ExecuteAsync(QuotinatorMigrations.Migration002_ReseedGenres);
            await conn.ExecuteAsync(QuotinatorMigrations.Migration003_ImportBatches);
            await conn.ExecuteAsync(QuotinatorMigrations.Migration004_ConsolidatedSinceV172Core + QuotinatorMigrations.CharacterGlobalIdentityMerge);

            var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            // Simulates a row that predates migration 4's ConflictPolicy column: its value is the
            // literal SQL DEFAULT 'skip', never touched by application code (which always writes
            // PascalCase via DuplicateResolutionPolicy.ToString()).
            await conn.ExecuteAsync(
                "INSERT INTO ImportBatches (Id, Name, Type, ImportedAt, RecordCount, DateCreated, IsDeleted, ConflictPolicy) " +
                "VALUES (@id, 'pre-check-constraint.json', 'Import', @now, 0, @now, 0, 'skip');",
                new { id = batchId, now });

            await conn.ExecuteAsync("CREATE TABLE System_ConsumerSchemaVersion (Version INTEGER NOT NULL, AppliedAt TEXT NOT NULL)");
            await conn.ExecuteAsync(
                "INSERT INTO System_ConsumerSchemaVersion (Version, AppliedAt) VALUES (1, @now), (2, @now), (3, @now), (4, @now);",
                new { now });
        }

        var db = CreateInitializer([]);
        await db.InitialiseAsync();

        using var verifyConn = new SqliteConnection($"Data Source={_dbPath}");
        await verifyConn.OpenAsync(TestContext.CancellationToken);

        var normalisedValue = await verifyConn.ExecuteScalarAsync<string>(
            "SELECT ConflictPolicy FROM Import_Batch WHERE Id = @id;", new { id = batchId });
        Assert.AreEqual("Skip", normalisedValue,
            "The legacy lowercase 'skip' default must be normalised to 'Skip' to satisfy the new CHECK constraint");

        await Assert.ThrowsExactlyAsync<SqliteException>(() => verifyConn.ExecuteAsync(
            "INSERT INTO Import_Batch (Id, Name, Type, ImportedAt, RecordCount, DateCreated, IsDeleted, ConflictPolicy) " +
            "VALUES (@id, 'post-check-constraint.json', 'Import', @now, 0, @now, 0, 'skip');",
            new { id = Guid.NewGuid().ToString(), now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") }),
            "The new CHECK constraint must reject the old lowercase form going forward");
    }

    public TestContext TestContext { get; set; }
}

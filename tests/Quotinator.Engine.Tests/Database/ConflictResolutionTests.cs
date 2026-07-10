using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Import;
using Quotinator.Data.Queries;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Engine.Database;
using Quotinator.Engine.Entities;
using Quotinator.Engine.Repositories;
using Quotinator.Engine.Services;

namespace Quotinator.Engine.Tests.Database;

/// <summary>
/// Content-level integration tests for #64's conflict-resolution policies, exercised through the
/// real seeding pipeline against small controlled fixture files (two files sharing one quote Id),
/// rather than the bundled real-world data used elsewhere in <see cref="DatabaseInitializerTests"/>.
/// </summary>
[TestClass]
public class ConflictResolutionTests
{
    private const string SharedId = "11111111-1111-1111-1111-111111111111";

    private string _tempDir = null!;
    private string _dbPath  = null!;
    private string _backups = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_conflict_test_").FullName;
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
        IReadOnlyList<SeedBatch> batches, ISystemChangeLogWriter? changeLogWriter = null)
    {
        var factory       = new SqliteConnectionFactory(_dbPath);
        var options       = new DatabaseOptions { DbPath = _dbPath, BackupsPath = _backups };
        var importBatches = new SqliteImportBatchRepository(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);
        var actionReader  = new SystemImportActionReader(factory);
        var actionWriter  = new SystemImportActionWriter(factory);
        var coordinator   = new ImportActionResolutionCoordinator(actionReader, actionWriter, factory);
        var actionService = new SqliteImportActionService(actionReader, coordinator, changeLogWriter ?? NoOpSystemChangeLogWriter.Instance,
            new SqliteRestorableRepository<QuoteEntity>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Source>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Character>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Person>(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance),
            importBatches, factory);
        return new QuotinatorDatabaseInitializer(factory, options, QuotinatorMigrations.All, batches, importBatches,
            coordinator, actionService,
            NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance, NullLogger<DatabaseInitializer>.Instance,
            NoOpSourceCacheUpdater.Instance, autoUpdateSources: false, QuotinatorMigrations.Baseline);
    }

    private string WriteQuoteFile(string name, string json)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, json);
        return path;
    }

    // First-seen file: no character, quote text "Original quote text", genre "drama".
    private string WriteFirstFile() => WriteQuoteFile("first.json", $$"""
        [{"id":"{{SharedId}}","quote":"Original quote text","source":"Same Source","date":"1990","type":"movie","genres":["drama"]}]
        """);

    // Incoming duplicate: adds a character (existing side is blank), and differs on quote text and
    // genres (both sides non-empty — a true conflict for those two fields).
    private string WriteSecondFile() => WriteQuoteFile("second.json", $$"""
        [{"id":"{{SharedId}}","quote":"Updated quote text","source":"Same Source","date":"1990","character":"Neo","type":"movie","genres":["comedy"]}]
        """);

    private async Task<(string QuoteText, string? Character, List<string> Genres)> ReadResultAsync()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();

        var row = await conn.QuerySingleAsync<(string QuoteText, string? Character)>(
            "SELECT q.QuoteText, c.Name AS Character FROM Quotes q " +
            "LEFT JOIN Characters c ON c.Id = q.CharacterId " +
            "WHERE q.Id = @id", new { id = SharedId });
        var genres = (await conn.QueryAsync<string>(
            "SELECT Genre FROM QuoteGenres WHERE QuoteId = @id", new { id = SharedId })).ToList();

        return (row.QuoteText, row.Character, genres);
    }

    [TestMethod]
    public async Task NewestWins_TrueConflictFields_SurvivingRowMatchesLaterFile()
    {
        var batch = new SeedBatch(
            [new SeedFile(WriteFirstFile(), null), new SeedFile(WriteSecondFile(), null)],
            new ManifestPolicy(DuplicateResolutionPolicy.NewestWins), "test");
        await CreateInitializer([batch]).InitialiseAsync();

        var (quoteText, character, genres) = await ReadResultAsync();

        Assert.AreEqual("Updated quote text", quoteText, "NewestWins replaces the whole record with the incoming one");
        Assert.AreEqual("Neo", character);
        CollectionAssert.AreEquivalent(new[] { "Comedy" }, genres);
    }

    [TestMethod]
    public async Task MergeOurs_AutoFillsBlankFieldAndKeepsExistingOnTrueConflict()
    {
        var batch = new SeedBatch(
            [new SeedFile(WriteFirstFile(), null), new SeedFile(WriteSecondFile(), null)],
            new ManifestPolicy(DuplicateResolutionPolicy.MergeOurs), "test");
        await CreateInitializer([batch]).InitialiseAsync();

        var (quoteText, character, genres) = await ReadResultAsync();

        Assert.AreEqual("Original quote text", quoteText, "True conflict on quote text — MergeOurs keeps the existing value");
        Assert.AreEqual("Neo", character, "Existing character was blank — auto-filled from the incoming side regardless of policy direction");
        CollectionAssert.AreEquivalent(new[] { "Drama" }, genres, "True conflict on genres (array field) — MergeOurs keeps the existing array wholesale, no union with the incoming array");
    }

    [TestMethod]
    public async Task MergeTheirs_AutoFillsBlankFieldAndTakesIncomingOnTrueConflict()
    {
        var batch = new SeedBatch(
            [new SeedFile(WriteFirstFile(), null), new SeedFile(WriteSecondFile(), null)],
            new ManifestPolicy(DuplicateResolutionPolicy.MergeTheirs), "test");
        await CreateInitializer([batch]).InitialiseAsync();

        var (quoteText, character, genres) = await ReadResultAsync();

        Assert.AreEqual("Updated quote text", quoteText, "True conflict on quote text — MergeTheirs takes the incoming value");
        Assert.AreEqual("Neo", character, "Existing character was blank — auto-filled from the incoming side regardless of policy direction");
        CollectionAssert.AreEquivalent(new[] { "Comedy" }, genres, "True conflict on genres (array field) — MergeTheirs takes the incoming array wholesale, no union with the existing array");
    }

    [TestMethod]
    public async Task Review_BehavesIdenticallyToSkip()
    {
        var batch = new SeedBatch(
            [new SeedFile(WriteFirstFile(), null), new SeedFile(WriteSecondFile(), null)],
            new ManifestPolicy(DuplicateResolutionPolicy.Review), "test");
        await CreateInitializer([batch]).InitialiseAsync();

        var (quoteText, character, genres) = await ReadResultAsync();

        Assert.AreEqual("Original quote text", quoteText, "Review does not auto-resolve — behaves exactly like Skip today");
        Assert.IsNull(character);
        CollectionAssert.AreEquivalent(new[] { "Drama" }, genres);
    }

    // ── #55: IsComplete / NoValueKnown ──────────────────────────────────────

    [TestMethod]
    public async Task Seed_FreshQuote_DefaultsIsCompleteFalseAndNoValueKnownEmpty()
    {
        var batch = new SeedBatch([new SeedFile(WriteFirstFile(), null)], new ManifestPolicy(DuplicateResolutionPolicy.NewestWins), "test");
        await CreateInitializer([batch]).InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var (isComplete, noValueKnown) = await conn.QuerySingleAsync<(long IsComplete, string NoValueKnown)>(
            "SELECT IsComplete, NoValueKnown FROM Quotes WHERE Id = @id", new { id = SharedId });

        Assert.AreEqual(0L, isComplete, "A brand-new row must default IsComplete to false");
        Assert.AreEqual("[]", noValueKnown, "A brand-new row must default NoValueKnown to an empty JSON array");
    }

    /// <summary>
    /// Regression guard for the exact production statement #64's conflict engine (and the live
    /// import service) uses to rewrite an existing row on newest-wins/merge-ours/merge-theirs — it
    /// must never include IsComplete/NoValueKnown in its SET list, or a human's completed review
    /// would be silently reset on every reseed/reimport that happens to touch that quote again.
    /// </summary>
    [TestMethod]
    public async Task UpdateOnNewestWins_NeverResetsIsCompleteOrNoValueKnown()
    {
        var batch = new SeedBatch([new SeedFile(WriteFirstFile(), null)], new ManifestPolicy(DuplicateResolutionPolicy.NewestWins), "test");
        await CreateInitializer([batch]).InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();

        await conn.ExecuteAsync(
            "UPDATE Quotes SET IsComplete = 1, NoValueKnown = '[\"date\"]' WHERE Id = @id",
            new { id = SharedId });

        var sourceId = await conn.ExecuteScalarAsync<string>("SELECT SourceId FROM Quotes WHERE Id = @id", new { id = SharedId });

        await conn.ExecuteAsync(Sql.Quotes.UpdateOnNewestWins, new
        {
            text    = "Rewritten by a later reseed",
            lang    = "en",
            sid     = sourceId,
            cid     = (string?)null,
            pid     = (string?)null,
            batchId = (string?)null,
            mod     = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            id      = SharedId
        });

        var (quoteText, isComplete, noValueKnown) = await conn.QuerySingleAsync<(string QuoteText, long IsComplete, string NoValueKnown)>(
            "SELECT QuoteText, IsComplete, NoValueKnown FROM Quotes WHERE Id = @id", new { id = SharedId });

        Assert.AreEqual("Rewritten by a later reseed", quoteText, "The statement must still update the fields it's meant to");
        Assert.AreEqual(1L, isComplete, "IsComplete must survive an UpdateOnNewestWins rewrite unchanged");
        Assert.AreEqual("[\"date\"]", noValueKnown, "NoValueKnown must survive an UpdateOnNewestWins rewrite unchanged");
    }

    [TestMethod]
    public void HardcodedDefault_IsNewestWins()
    {
        Assert.AreEqual(DuplicateResolutionPolicy.NewestWins, ManifestPolicy.HardcodedDefault.Default,
            "Regression guard: #64 flips the fallback default from Skip to NewestWins");
    }

    // ── System_ImportConflicts ───────────────────────────────────────────────
    //
    // Seeding no longer writes to System_ImportConflicts — #154 replaced its per-row conflict
    // logging with System_ImportActions (ImportActionPlanner/SqliteImportActionService), so the
    // seeding-integration tests that used to assert on System_ImportConflicts content here were
    // removed. The equivalent through-the-seeding-pipeline classification coverage now lives in
    // ImportActionPlannerTests (Add/Modify/Pending-Modify classification) and
    // SqliteImportActionServiceTests (apply/decide). System_ImportConflicts and
    // SqliteConflictResolutionService (#149) have been removed entirely (#154 Phase B) — the
    // System_ImportConflicts table's migration/baseline history is untouched (squashing it is #155's
    // call), but no C# code reads or writes it any more.

    // ── #56: System_ChangeLog ────────────────────────────────────────────────

    [TestMethod]
    public async Task Seed_FreshQuote_WritesCreatedChangeLogRowsWithSeedInitiator()
    {
        var factory    = new SqliteConnectionFactory(_dbPath);
        var writer     = new SystemChangeLogWriter(factory);
        var batch      = new SeedBatch([new SeedFile(WriteFirstFile(), null)], new ManifestPolicy(DuplicateResolutionPolicy.NewestWins), "test");
        await CreateInitializer([batch], changeLogWriter: writer).InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var rows = (await conn.QueryAsync<(string EntityType, string EntityId, string InitiatedByType, string Action)>(
            "SELECT EntityType, EntityId, InitiatedByType, Action FROM System_ChangeLog")).ToList();

        var quoteRow = rows.Single(r => r.EntityType == "quote");
        Assert.AreEqual(SharedId, quoteRow.EntityId);
        Assert.AreEqual("Seed", quoteRow.InitiatedByType);
        Assert.AreEqual("Created", quoteRow.Action);

        var sourceRow = rows.Single(r => r.EntityType == "source");
        Assert.AreEqual("Seed", sourceRow.InitiatedByType);
        Assert.AreEqual("Created", sourceRow.Action);
    }

    [TestMethod]
    public async Task NewestWins_CrossFileDuplicate_WritesModifiedChangeLogRowForQuote()
    {
        var factory = new SqliteConnectionFactory(_dbPath);
        var writer  = new SystemChangeLogWriter(factory);
        var batch   = new SeedBatch(
            [new SeedFile(WriteFirstFile(), null), new SeedFile(WriteSecondFile(), null)],
            new ManifestPolicy(DuplicateResolutionPolicy.NewestWins), "test");
        await CreateInitializer([batch], changeLogWriter: writer).InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var quoteActions = (await conn.QueryAsync<string>(
            "SELECT Action FROM System_ChangeLog WHERE EntityType = 'quote' AND EntityId = @id ORDER BY OccurredAt",
            new { id = SharedId })).ToList();

        CollectionAssert.AreEqual(new[] { "Created", "Modified" }, quoteActions,
            "The first file's insert logs Created; the second file's newest-wins rewrite logs Modified");
    }

    [TestMethod]
    [DataRow(DuplicateResolutionPolicy.Skip)]
    [DataRow(DuplicateResolutionPolicy.Review)]
    public async Task SkipOrReview_CrossFileDuplicate_WritesNoModifiedChangeLogRow(DuplicateResolutionPolicy policy)
    {
        var factory = new SqliteConnectionFactory(_dbPath);
        var writer  = new SystemChangeLogWriter(factory);
        var batch   = new SeedBatch(
            [new SeedFile(WriteFirstFile(), null), new SeedFile(WriteSecondFile(), null)],
            new ManifestPolicy(policy), "test");
        await CreateInitializer([batch], changeLogWriter: writer).InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var quoteActions = (await conn.QueryAsync<string>(
            "SELECT Action FROM System_ChangeLog WHERE EntityType = 'quote' AND EntityId = @id",
            new { id = SharedId })).ToList();

        CollectionAssert.AreEqual(new[] { "Created" }, quoteActions,
            $"{policy} never executes the UPDATE, so no Modified row should exist — only the first file's Created row");
    }

    /// <summary>System_ChangeLog is System_-prefixed protected infrastructure — a Reset must never drop or replay it, same as System_AuditEntries/System_ImportConflicts.</summary>
    [TestMethod]
    public async Task ResetAsync_PreservesExistingChangeLogRows()
    {
        var factory = new SqliteConnectionFactory(_dbPath);
        var writer  = new SystemChangeLogWriter(factory);
        var batch   = new SeedBatch([new SeedFile(WriteFirstFile(), null)], new ManifestPolicy(DuplicateResolutionPolicy.NewestWins), "test");
        var db      = CreateInitializer([batch], changeLogWriter: writer);
        await db.InitialiseAsync();

        int countBeforeReset;
        using (var preConn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            preConn.Open();
            countBeforeReset = await preConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM System_ChangeLog;");
        }

        await db.ResetAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var countAfterReset = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM System_ChangeLog;");

        Assert.IsTrue(countAfterReset >= countBeforeReset * 2,
            "The pre-Reset rows must survive Reset, plus at least as many new rows from the re-seed pass");
    }
}

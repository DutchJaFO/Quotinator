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
using Quotinator.Engine.Repositories;

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

    private QuotinatorDatabaseInitializer CreateInitializer(IReadOnlyList<SeedBatch> batches, ISystemImportConflictWriter? conflictWriter = null)
    {
        var factory       = new SqliteConnectionFactory(_dbPath);
        var options       = new DatabaseOptions { DbPath = _dbPath, BackupsPath = _backups };
        var importBatches = new SqliteImportBatchRepository(factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);
        return new QuotinatorDatabaseInitializer(factory, options, QuotinatorMigrations.All, batches, importBatches,
            conflictWriter ?? NoOpSystemImportConflictWriter.Instance,
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

    [TestMethod]
    [DataRow(DuplicateResolutionPolicy.Skip,        ImportConflictStatus.Resolved)]
    [DataRow(DuplicateResolutionPolicy.NewestWins,  ImportConflictStatus.Resolved)]
    [DataRow(DuplicateResolutionPolicy.MergeOurs,   ImportConflictStatus.Resolved)]
    [DataRow(DuplicateResolutionPolicy.MergeTheirs, ImportConflictStatus.Resolved)]
    [DataRow(DuplicateResolutionPolicy.Review,      ImportConflictStatus.Pending)]
    public async Task SystemImportConflicts_LogsOneRowPerConflict_WithCorrectStatus(
        DuplicateResolutionPolicy policy, string expectedStatus)
    {
        var factory = new SqliteConnectionFactory(_dbPath);
        var writer  = new SystemImportConflictWriter(factory);
        var batch   = new SeedBatch([new SeedFile(WriteFirstFile(), null), new SeedFile(WriteSecondFile(), null)], new ManifestPolicy(policy), "test");
        await CreateInitializer([batch], writer).InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var rows = (await conn.QueryAsync<(string EntityId, string AppliedPolicy, string Status)>(
            "SELECT EntityId, AppliedPolicy, Status FROM System_ImportConflicts")).ToList();

        Assert.AreEqual(1, rows.Count, "Exactly one conflict row for the one duplicate quote");
        Assert.AreEqual(SharedId, rows[0].EntityId);
        Assert.AreEqual(policy.ToString(), rows[0].AppliedPolicy);
        Assert.AreEqual(expectedStatus, rows[0].Status);
    }

    [TestMethod]
    public async Task SystemImportConflicts_MergedFields_PopulatedOnlyForMergePolicies()
    {
        var factory = new SqliteConnectionFactory(_dbPath);
        var writer  = new SystemImportConflictWriter(factory);
        var batch   = new SeedBatch([new SeedFile(WriteFirstFile(), null), new SeedFile(WriteSecondFile(), null)],
            new ManifestPolicy(DuplicateResolutionPolicy.MergeTheirs), "test");
        await CreateInitializer([batch], writer).InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var mergedFields = await conn.ExecuteScalarAsync<string>(
            "SELECT MergedFields FROM System_ImportConflicts WHERE EntityId = @id", new { id = SharedId });

        Assert.IsNotNull(mergedFields);
        StringAssert.Contains(mergedFields, "\"quoteText\":\"theirs\"", "quoteText was a true conflict resolved via MergeTheirs");
        StringAssert.Contains(mergedFields, "\"genres\":\"theirs\"", "genres was a true conflict resolved via MergeTheirs");
        StringAssert.Contains(mergedFields, "\"character\":\"theirs\"", "character was auto-filled from the incoming side");
    }

    [TestMethod]
    public async Task SystemImportConflicts_NonMergePolicy_MergedFieldsIsNull()
    {
        var factory = new SqliteConnectionFactory(_dbPath);
        var writer  = new SystemImportConflictWriter(factory);
        var batch   = new SeedBatch([new SeedFile(WriteFirstFile(), null), new SeedFile(WriteSecondFile(), null)],
            new ManifestPolicy(DuplicateResolutionPolicy.NewestWins), "test");
        await CreateInitializer([batch], writer).InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var mergedFields = await conn.ExecuteScalarAsync<string?>(
            "SELECT MergedFields FROM System_ImportConflicts WHERE EntityId = @id", new { id = SharedId });

        Assert.IsNull(mergedFields, "MergedFields is populated only for MergeOurs/MergeTheirs resolutions");
    }

    /// <summary>System_ImportConflicts is System_-prefixed protected infrastructure — a Reset must never drop or replay it, same as System_AuditEntries.</summary>
    [TestMethod]
    public async Task ResetAsync_PreservesExistingImportConflictRows()
    {
        var factory = new SqliteConnectionFactory(_dbPath);
        var writer  = new SystemImportConflictWriter(factory);
        var batch   = new SeedBatch([new SeedFile(WriteFirstFile(), null), new SeedFile(WriteSecondFile(), null)],
            new ManifestPolicy(DuplicateResolutionPolicy.Review), "test");
        var db      = CreateInitializer([batch], writer);
        await db.InitialiseAsync();

        await db.ResetAsync();

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM System_ImportConflicts;");

        // 2, not 1: Reset drops and rebuilds the domain schema, then re-seeds from the same source
        // files — the seeding pass re-detects the same cross-file duplicate and logs a second row.
        // The point of this test is that the row from *before* the Reset survives at all (Reset never
        // drops System_-prefixed tables) — not that seeding is idempotent across a Reset.
        Assert.AreEqual(2, count, "The pre-Reset conflict row must survive Reset, plus one new row from the re-seed pass detecting the same duplicate again");
    }
}

using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Import;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Engine.Database;
using Quotinator.Engine.Repositories;
using Quotinator.Engine.Services;

namespace Quotinator.Engine.Tests.Services;

/// <summary>
/// Integration tests for <see cref="SqliteQuoteImportService"/> — the live
/// <c>POST /api/v1/import</c>/<c>.../import/preview</c> pipeline. Unlike
/// <see cref="Database.ConflictResolutionTests"/> (which detects duplicates across two files in the
/// same seeding pass), these tests exercise duplicate detection against a quote that already exists
/// in the database from a prior, separate import call — the scenario specific to this service.
/// </summary>
[TestClass]
public class QuoteImportServiceTests
{
    private const string SharedId = "11111111-1111-1111-1111-111111111111";

    private string _tempDir = null!;
    private string _dbPath  = null!;
    private string _backups = null!;
    private SqliteConnectionFactory _factory = null!;

    [TestInitialize]
    public async Task TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_import_svc_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");
        _backups = Path.Combine(_tempDir, "backups");
        _factory = new SqliteConnectionFactory(_dbPath);

        var options       = new DatabaseOptions { DbPath = _dbPath, BackupsPath = _backups };
        var importBatches = new SqliteImportBatchRepository(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);
        var actionReader  = new SystemImportActionReader(_factory);
        var actionWriter  = new SystemImportActionWriter(_factory);
        var coordinator   = new ImportActionResolutionCoordinator(actionReader, actionWriter, _factory);
        var actionService = new SqliteImportActionService(actionReader, coordinator, NoOpSystemChangeLogWriter.Instance);
        var db = new QuotinatorDatabaseInitializer(
            _factory, options, QuotinatorMigrations.All, [], importBatches,
            coordinator, actionService,
            NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance,
            NullLogger<DatabaseInitializer>.Instance, NoOpSourceCacheUpdater.Instance,
            autoUpdateSources: false, QuotinatorMigrations.Baseline);
        await db.InitialiseAsync();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private SqliteQuoteImportService CreateService(
        ISystemChangeLogWriter? changeLogWriter = null,
        IReadOnlyDictionary<string, IQuoteSourceConverter>? converters = null,
        ManifestPolicy? configPolicy = null)
    {
        var importBatches  = new SqliteImportBatchRepository(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);
        var actionReader   = new SystemImportActionReader(_factory);
        var actionWriter   = new SystemImportActionWriter(_factory);
        var coordinator    = new ImportActionResolutionCoordinator(actionReader, actionWriter, _factory);
        var actionService  = new SqliteImportActionService(actionReader, coordinator, changeLogWriter ?? NoOpSystemChangeLogWriter.Instance);
        return new SqliteQuoteImportService(
            _factory, importBatches, coordinator, actionService,
            converters ?? new Dictionary<string, IQuoteSourceConverter>(StringComparer.OrdinalIgnoreCase),
            configPolicy ?? new ManifestPolicy(DuplicateResolutionPolicy.NewestWins));
    }

    private static Stream JsonStream(string json) => new MemoryStream(Encoding.UTF8.GetBytes(json));

    private static string OneQuoteJson(string quote, string source, string? character = null, string[]? genres = null) =>
        JsonSerializer.Serialize(new[]
        {
            new { id = SharedId, quote, source, character, genres = genres ?? [] }
        });

    private async Task<int> CountAsync(string table)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {table}");
    }

    private async Task<string> ReadQuoteTextAsync()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return (await conn.ExecuteScalarAsync<string>("SELECT QuoteText FROM Quotes WHERE Id = @id", new { id = SharedId }))!;
    }

    // ── Fresh insert ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ImportAsync_FreshDatabase_InsertsNewQuote()
    {
        var service = CreateService();

        var result = await service.ImportAsync(JsonStream(OneQuoteJson("A quote.", "A Source")), "test.json", null, preview: false);

        Assert.AreEqual(1, result.Summary.Total);
        Assert.AreEqual(1, result.Summary.Imported);
        Assert.AreEqual(0, result.Summary.Updated);
        Assert.AreEqual(0, result.Summary.Errors);
        Assert.IsNotNull(result.BatchId);
        Assert.AreEqual(1, await CountAsync("Quotes"));
        Assert.AreEqual(1, await CountAsync("ImportBatches"));
        Assert.AreEqual("newest-wins", result.ConflictPolicy, "Response-facing wire value must be kebab-case, matching every other DuplicateResolutionPolicy JSON value in this API");
    }

    // ── Conflict policies against a pre-existing DB row (not a second file) ────

    [TestMethod]
    public async Task ImportAsync_Skip_KeepsExistingRowUnchanged()
    {
        var service = CreateService();
        await service.ImportAsync(JsonStream(OneQuoteJson("Original.", "A Source")), "first.json", null, preview: false);

        var settings = new ImportRequestSettingsDto { DuplicateResolution = new ManifestPolicyDto { Default = DuplicateResolutionPolicy.Skip } };
        var result = await service.ImportAsync(JsonStream(OneQuoteJson("Updated.", "A Source")), "second.json", settings, preview: false);

        Assert.AreEqual(0, result.Summary.Imported);
        Assert.AreEqual(0, result.Summary.Updated);
        Assert.AreEqual(1, result.Summary.Skipped);
        Assert.AreEqual("Original.", await ReadQuoteTextAsync());
    }

    [TestMethod]
    public async Task ImportAsync_NewestWins_ReplacesExistingRow()
    {
        var service = CreateService();
        await service.ImportAsync(JsonStream(OneQuoteJson("Original.", "A Source")), "first.json", null, preview: false);

        var settings = new ImportRequestSettingsDto { DuplicateResolution = new ManifestPolicyDto { Default = DuplicateResolutionPolicy.NewestWins } };
        var result = await service.ImportAsync(JsonStream(OneQuoteJson("Updated.", "A Source")), "second.json", settings, preview: false);

        Assert.AreEqual(1, result.Summary.Updated);
        Assert.AreEqual("Updated.", await ReadQuoteTextAsync());
    }

    [TestMethod]
    public async Task ImportAsync_MergeOurs_TrueConflictKeepsExisting()
    {
        var service = CreateService();
        await service.ImportAsync(JsonStream(OneQuoteJson("Original.", "A Source")), "first.json", null, preview: false);

        var settings = new ImportRequestSettingsDto { DuplicateResolution = new ManifestPolicyDto { Default = DuplicateResolutionPolicy.MergeOurs } };
        var result = await service.ImportAsync(JsonStream(OneQuoteJson("Updated.", "A Source")), "second.json", settings, preview: false);

        Assert.AreEqual(1, result.Summary.Updated);
        Assert.AreEqual("Original.", await ReadQuoteTextAsync());
    }

    [TestMethod]
    public async Task ImportAsync_MergeTheirs_TrueConflictTakesIncoming()
    {
        var service = CreateService();
        await service.ImportAsync(JsonStream(OneQuoteJson("Original.", "A Source")), "first.json", null, preview: false);

        var settings = new ImportRequestSettingsDto { DuplicateResolution = new ManifestPolicyDto { Default = DuplicateResolutionPolicy.MergeTheirs } };
        var result = await service.ImportAsync(JsonStream(OneQuoteJson("Updated.", "A Source")), "second.json", settings, preview: false);

        Assert.AreEqual(1, result.Summary.Updated);
        Assert.AreEqual("Updated.", await ReadQuoteTextAsync());
        Assert.AreEqual("merge-theirs", result.Conflicts.Single().AppliedPolicy, "Response-facing wire value must be kebab-case, matching every other DuplicateResolutionPolicy JSON value in this API");
    }

    // ── #55: IsComplete / NoValueKnown ──────────────────────────────────────

    [TestMethod]
    public async Task ImportAsync_FreshDatabase_DefaultsIsCompleteFalseAndNoValueKnownEmpty()
    {
        var service = CreateService();
        await service.ImportAsync(JsonStream(OneQuoteJson("A quote.", "A Source")), "test.json", null, preview: false);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var (isComplete, noValueKnown) = await conn.QuerySingleAsync<(long IsComplete, string NoValueKnown)>(
            "SELECT IsComplete, NoValueKnown FROM Quotes WHERE Id = @id", new { id = SharedId });

        Assert.AreEqual(0L, isComplete, "A brand-new row must default IsComplete to false");
        Assert.AreEqual("[]", noValueKnown, "A brand-new row must default NoValueKnown to an empty JSON array");
    }

    [TestMethod]
    [DataRow(DuplicateResolutionPolicy.NewestWins)]
    [DataRow(DuplicateResolutionPolicy.MergeOurs)]
    [DataRow(DuplicateResolutionPolicy.MergeTheirs)]
    public async Task ImportAsync_ExistingRowMarkedComplete_SurvivesReimportUnchanged(DuplicateResolutionPolicy policy)
    {
        var service = CreateService();
        await service.ImportAsync(JsonStream(OneQuoteJson("Original.", "A Source")), "first.json", null, preview: false);

        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            await conn.ExecuteAsync(
                "UPDATE Quotes SET IsComplete = 1, NoValueKnown = '[\"date\"]' WHERE Id = @id",
                new { id = SharedId });
        }

        var settings = new ImportRequestSettingsDto { DuplicateResolution = new ManifestPolicyDto { Default = policy } };
        var result = await service.ImportAsync(JsonStream(OneQuoteJson("Updated.", "A Source")), "second.json", settings, preview: false);

        Assert.AreEqual(1, result.Summary.Updated, "The duplicate must still be resolved (row rewritten), not skipped");

        using var conn2 = new SqliteConnection($"Data Source={_dbPath}");
        conn2.Open();
        var (isComplete, noValueKnown) = await conn2.QuerySingleAsync<(long IsComplete, string NoValueKnown)>(
            "SELECT IsComplete, NoValueKnown FROM Quotes WHERE Id = @id", new { id = SharedId });

        Assert.AreEqual(1L, isComplete, "A human's completed review must survive a re-import that rewrites the row");
        Assert.AreEqual("[\"date\"]", noValueKnown, "Confirmed no-value-known markers must survive a re-import that rewrites the row");
    }

    [TestMethod]
    public async Task ImportAsync_Review_BehavesLikeSkip()
    {
        var service = CreateService();
        await service.ImportAsync(JsonStream(OneQuoteJson("Original.", "A Source")), "first.json", null, preview: false);

        var settings = new ImportRequestSettingsDto { DuplicateResolution = new ManifestPolicyDto { Default = DuplicateResolutionPolicy.Review } };
        var result = await service.ImportAsync(JsonStream(OneQuoteJson("Updated.", "A Source")), "second.json", settings, preview: false);

        Assert.AreEqual(1, result.Summary.Skipped);
        Assert.AreEqual("Original.", await ReadQuoteTextAsync());
        Assert.AreEqual("pending", result.Conflicts.Single().Status);
    }

    // ── #56: System_ChangeLog ────────────────────────────────────────────────

    [TestMethod]
    public async Task ImportAsync_FreshDatabase_WritesCreatedChangeLogRowWithImportInitiator()
    {
        var changeLogWriter = new SystemChangeLogWriter(_factory);
        var service = CreateService(changeLogWriter: changeLogWriter);

        var result = await service.ImportAsync(JsonStream(OneQuoteJson("A quote.", "A Source")), "test.json", null, preview: false);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var row = await conn.QuerySingleAsync<(string InitiatedByType, string InitiatedById, string Action)>(
            "SELECT InitiatedByType, InitiatedById, Action FROM System_ChangeLog WHERE EntityType = 'quote' AND EntityId = @id",
            new { id = SharedId });

        Assert.AreEqual("Import", row.InitiatedByType);
        Assert.AreEqual(result.BatchId!.Value.ToString("D").ToUpperInvariant(), row.InitiatedById);
        Assert.AreEqual("Created", row.Action);
    }

    [TestMethod]
    public async Task ImportAsync_NewestWins_WritesModifiedChangeLogRowWithSameImportBatchId()
    {
        var changeLogWriter = new SystemChangeLogWriter(_factory);
        var service = CreateService(changeLogWriter: changeLogWriter);
        await service.ImportAsync(JsonStream(OneQuoteJson("Original.", "A Source")), "first.json", null, preview: false);

        var settings = new ImportRequestSettingsDto { DuplicateResolution = new ManifestPolicyDto { Default = DuplicateResolutionPolicy.NewestWins } };
        var result = await service.ImportAsync(JsonStream(OneQuoteJson("Updated.", "A Source")), "second.json", settings, preview: false);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var rows = (await conn.QueryAsync<(string InitiatedById, string Action)>(
            "SELECT InitiatedById, Action FROM System_ChangeLog WHERE EntityType = 'quote' AND EntityId = @id ORDER BY OccurredAt",
            new { id = SharedId })).ToList();

        Assert.AreEqual(2, rows.Count, "One Created row from the first import, one Modified row from the newest-wins rewrite");
        Assert.AreEqual("Created", rows[0].Action);
        Assert.AreEqual("Modified", rows[1].Action);
        Assert.AreEqual(result.BatchId!.Value.ToString("D").ToUpperInvariant(), rows[1].InitiatedById,
            "The Modified row's InitiatedById must be the second import's own batch, not the first");
    }

    [TestMethod]
    public async Task ImportAsync_Skip_WritesNoModifiedChangeLogRow()
    {
        var changeLogWriter = new SystemChangeLogWriter(_factory);
        var service = CreateService(changeLogWriter: changeLogWriter);
        await service.ImportAsync(JsonStream(OneQuoteJson("Original.", "A Source")), "first.json", null, preview: false);

        var settings = new ImportRequestSettingsDto { DuplicateResolution = new ManifestPolicyDto { Default = DuplicateResolutionPolicy.Skip } };
        await service.ImportAsync(JsonStream(OneQuoteJson("Updated.", "A Source")), "second.json", settings, preview: false);

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var actions = (await conn.QueryAsync<string>(
            "SELECT Action FROM System_ChangeLog WHERE EntityType = 'quote' AND EntityId = @id",
            new { id = SharedId })).ToList();

        CollectionAssert.AreEqual(new[] { "Created" }, actions, "Skip never executes the UPDATE, so no Modified row should exist");
    }

    [TestMethod]
    public async Task ImportAsync_PreviewWithNewRow_NoChangeLogRowPersisted()
    {
        var changeLogWriter = new SystemChangeLogWriter(_factory);
        var service = CreateService(changeLogWriter: changeLogWriter);

        await service.ImportAsync(JsonStream(OneQuoteJson("A quote.", "A Source")), "test.json", null, preview: true);

        Assert.AreEqual(0, await CountAsync("System_ChangeLog"), "Rolled back — no change-log row persisted for a preview run");
    }

    // ── Preview ──────────────────────────────────────────────────────────────

    // #154 revision: preview now stages a real, inspectable batch instead of rolling everything
    // back — "nothing persisted" is no longer the contract (see 154-import-staging-plan.md Section
    // "Explicit behavior changes"). These two tests are updated, not left unmodified, because they
    // directly assert the old rollback contract.

    [TestMethod]
    public async Task ImportAsync_Preview_StagesButNeverApplies()
    {
        var service = CreateService();

        var result = await service.ImportAsync(JsonStream(OneQuoteJson("A quote.", "A Source")), "test.json", null, preview: true);

        Assert.IsTrue(result.Preview);
        Assert.IsNotNull(result.BatchId, "Preview now stages a real batch, unlike the old rollback contract");
        Assert.AreEqual(1, result.Summary.Imported, "Response still reports what would have happened");
        Assert.AreEqual(0, await CountAsync("Quotes"), "Staging never applies — no quote written");
        Assert.AreEqual(1, await CountAsync("ImportBatches"), "The batch itself is durably staged");
        Assert.AreEqual(2, await CountAsync("System_ImportActions"), "The planned Quote and Source Add actions are both durably staged");
    }

    [TestMethod]
    public async Task ImportAsync_PreviewWithConflict_StagesButDoesNotApply()
    {
        var service = CreateService();
        await service.ImportAsync(JsonStream(OneQuoteJson("Original.", "A Source")), "first.json", null, preview: false);

        var result = await service.ImportAsync(JsonStream(OneQuoteJson("Updated.", "A Source")), "second.json", null, preview: true);

        Assert.AreEqual(1, result.Conflicts.Count, "Response reflects the conflict that would have been detected");
        Assert.IsTrue(await CountAsync("System_ImportActions") > 0, "The Modify action is durably staged, not rolled back");
        Assert.AreEqual("Original.", await ReadQuoteTextAsync(), "Never applied — original row untouched");
    }

    // ── Row-level error tolerance ────────────────────────────────────────────

    [TestMethod]
    public async Task ImportAsync_OneRowMissingSource_SkipsItButImportsTheRest()
    {
        var service = CreateService();
        var json = """
            [
              {"id":"11111111-1111-1111-1111-111111111111","quote":"Missing a source","source":""},
              {"id":"22222222-2222-2222-2222-222222222222","quote":"A real quote.","source":"A Real Source"}
            ]
            """;

        var result = await service.ImportAsync(JsonStream(json), "test.json", null, preview: false);

        Assert.AreEqual(2, result.Summary.Total);
        Assert.AreEqual(1, result.Summary.Imported);
        Assert.AreEqual(1, result.Summary.Errors);
        Assert.AreEqual(1, result.Errors.Single().Row);
        Assert.AreEqual(1, await CountAsync("Quotes"));
    }

    [TestMethod]
    public async Task ImportAsync_FileWithNoQuotes_ThrowsQuoteImportValidationException()
    {
        var service = CreateService();

        await Assert.ThrowsExactlyAsync<QuoteImportValidationException>(
            () => service.ImportAsync(JsonStream("[]"), "test.json", null, preview: false));
    }

    [TestMethod]
    public async Task ImportAsync_MalformedJson_ThrowsQuoteImportValidationException()
    {
        var service = CreateService();

        await Assert.ThrowsExactlyAsync<QuoteImportValidationException>(
            () => service.ImportAsync(JsonStream("{ not json"), "test.json", null, preview: false));
    }

    // ── Converter path ───────────────────────────────────────────────────────

    [TestMethod]
    public async Task ImportAsync_UnknownConverterName_ThrowsUnknownConverterException()
    {
        var service = CreateService();
        var settings = new ImportRequestSettingsDto { Converter = "does-not-exist" };

        var ex = await Assert.ThrowsExactlyAsync<UnknownConverterException>(
            () => service.ImportAsync(JsonStream("irrelevant"), "test.json", settings, preview: false));
        Assert.AreEqual("does-not-exist", ex.ConverterName);
    }

    [TestMethod]
    public async Task ImportAsync_RegisteredConverter_ConvertsBeforeImporting()
    {
        var converters = new Dictionary<string, IQuoteSourceConverter>(StringComparer.OrdinalIgnoreCase)
        {
            ["passthrough"] = new PassthroughTestConverter()
        };
        var service = CreateService(converters: converters);
        var settings = new ImportRequestSettingsDto { Converter = "passthrough" };

        var result = await service.ImportAsync(
            JsonStream(OneQuoteJson("Converted quote.", "A Source")), "raw.txt", settings, preview: false);

        Assert.AreEqual(1, result.Summary.Imported);
        Assert.AreEqual("Converted quote.", await ReadQuoteTextAsync());
    }

    /// <summary>Trivial converter that copies its input verbatim — the input is already canonical JSON for this test's purposes.</summary>
    private sealed class PassthroughTestConverter : IQuoteSourceConverter
    {
        public string Name => "passthrough";

        public async Task ConvertAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default)
            => await File.WriteAllTextAsync(outputPath, await File.ReadAllTextAsync(inputPath, cancellationToken), cancellationToken);
    }
}

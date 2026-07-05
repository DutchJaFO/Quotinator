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
/// <c>POST /api/v1/quotes/import</c>/<c>.../import/preview</c> pipeline. Unlike
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
        var db = new QuotinatorDatabaseInitializer(
            _factory, options, QuotinatorMigrations.All, [], importBatches,
            NoOpSystemImportConflictWriter.Instance, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance,
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
        ISystemImportConflictWriter? conflictWriter = null,
        IReadOnlyDictionary<string, IQuoteSourceConverter>? converters = null,
        ManifestPolicy? configPolicy = null)
    {
        var importBatches = new SqliteImportBatchRepository(_factory, NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance);
        return new SqliteQuoteImportService(
            _factory, importBatches,
            conflictWriter ?? NoOpSystemImportConflictWriter.Instance,
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

    // ── Preview ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ImportAsync_Preview_LeavesZeroTrace()
    {
        var conflictWriter = new SystemImportConflictWriter(_factory);
        var service = CreateService(conflictWriter);

        var result = await service.ImportAsync(JsonStream(OneQuoteJson("A quote.", "A Source")), "test.json", null, preview: true);

        Assert.IsTrue(result.Preview);
        Assert.IsNull(result.BatchId);
        Assert.AreEqual(1, result.Summary.Imported, "Response still reports what would have happened");
        Assert.AreEqual(0, await CountAsync("Quotes"), "No quote persisted");
        Assert.AreEqual(0, await CountAsync("ImportBatches"), "No batch persisted");
        Assert.AreEqual(0, await CountAsync("System_ImportConflicts"), "No conflict rows persisted");
    }

    [TestMethod]
    public async Task ImportAsync_PreviewWithConflict_NoConflictRowsPersisted()
    {
        var conflictWriter = new SystemImportConflictWriter(_factory);
        var service = CreateService(conflictWriter);
        await service.ImportAsync(JsonStream(OneQuoteJson("Original.", "A Source")), "first.json", null, preview: false);

        var result = await service.ImportAsync(JsonStream(OneQuoteJson("Updated.", "A Source")), "second.json", null, preview: true);

        Assert.AreEqual(1, result.Conflicts.Count, "Response reflects the conflict that would have been detected");
        Assert.AreEqual(0, await CountAsync("System_ImportConflicts"), "Rolled back — nothing persisted");
        Assert.AreEqual("Original.", await ReadQuoteTextAsync(), "Rolled back — original row untouched");
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

using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Core.Database;
using Quotinator.Core.Entities;
using Quotinator.Core.Services;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Enums;
using Quotinator.Data.Import;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Core.Tests.Database;

/// <summary>
/// Proves two gaps found live after #251's own T2 pass: <c>manifest.json</c>'s own content was never
/// captured as a <c>FileResource</c> despite driving the whole seed plan, and a <c>SeedFile</c>'s
/// <c>Converter</c>/<c>ConverterOptions</c> were never recorded anywhere. Uses a real
/// <see cref="SqliteFileResourceRepository"/> (not <see cref="NoOpFileResourceRepository"/>, which every
/// other <c>QuotinatorDatabaseInitializer</c> test in this project uses) so the actual write path runs.
/// </summary>
[TestClass]
public class FileResourceCaptureTests
{
    private const string MinimalQuoteJson = """
        [
          {
            "id": "11111111-1111-1111-1111-111111111111",
            "quote": "Test quote.",
            "originalLanguage": "en",
            "source": "Test Source",
            "date": "2000",
            "type": "movie",
            "genres": ["drama"]
          }
        ]
        """;

    private string _tempDir    = null!;
    private string _sourcesDir = null!;
    private string _dbPath     = null!;
    private string _backups    = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _tempDir    = Directory.CreateTempSubdirectory("quotinator_fileresource_capture_test_").FullName;
        _sourcesDir = Path.Combine(_tempDir, "sources");
        Directory.CreateDirectory(_sourcesDir);
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

    private async Task InitialiseAsync(IReadOnlyList<SeedBatch> batches, IFileResourceRepository fileResources)
    {
        SqliteConnectionFactory factory        = new SqliteConnectionFactory(_dbPath);
        DatabaseOptions options         = new DatabaseOptions { DbPath = _dbPath, BackupsPath = _backups };
        SqliteImportBatchRepository importBatches   = new SqliteImportBatchRepository(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance);
        ImportActionReader actionReader    = new ImportActionReader(factory);
        ImportActionWriter actionWriter    = new ImportActionWriter(factory);
        ImportActionResolutionCoordinator coordinator     = new ImportActionResolutionCoordinator(actionReader, actionWriter, factory);
        SqliteImportActionService actionService   = new SqliteImportActionService(actionReader, coordinator, actionWriter, NoOpAuditEntryWriter.Instance, NoOpChangeWriter.Instance,
            new SqliteRestorableRepository<QuoteEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<SourceEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<CharacterEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<PersonEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<ConversationEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<StageDirectionEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<SoundCueEntity>(factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            importBatches, factory, NoOpNotificationWriter.Instance);

        QuotinatorDatabaseInitializer db = new QuotinatorDatabaseInitializer(factory, options, QuotinatorMigrations.All, batches, importBatches,
            coordinator, actionService, actionWriter, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance,
            NullLogger<DatabaseInitializer>.Instance, NoOpSourceCacheUpdater.Instance, autoUpdateSources: false,
            autoPurgeBundledImportActions: false, autoPurgeUserImportActions: false,
            NoOpRuleFileOverridePathResolver.Instance, NoOpSourceFileOverrideRegistry.Instance,
            fileResources,
            NoOpNotificationReader.Instance, NoOpNotificationWriter.Instance, NoOpNotificationTextSource.Instance,
            new AppVersionTracker(factory), new VersionService(), NoOpDiskSpaceProvider.Instance,
            QuotinatorMigrations.Baseline);

        await db.InitialiseAsync();
    }

    [TestMethod]
    public async Task InitialiseAsync_ManifestJsonPresentInSourceDir_CapturesItsOwnContentLinkedToTheBatch()
    {
        string quotesPath = Path.Combine(_sourcesDir, "quotes.json");
        File.WriteAllText(quotesPath, MinimalQuoteJson);
        string manifestPath = Path.Combine(_sourcesDir, ManifestSeedPlanner.ManifestFileName);
        File.WriteAllText(manifestPath, """{ "files": [ { "file": "quotes.json", "name": "quotes" } ] }""");

        SqliteFileResourceRepository fileResources = new SqliteFileResourceRepository(new SqliteConnectionFactory(_dbPath));
        SeedBatch batch = new SeedBatch([new SeedFile(quotesPath, null)], ManifestPolicy.HardcodedDefault, "test");

        await InitialiseAsync([batch], fileResources);

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        dynamic? manifestRow = await conn.QuerySingleOrDefaultAsync(
            "SELECT Id, FileName FROM Import_FileResource WHERE FileName = @name AND IsDeleted = 0;",
            new { name = ManifestSeedPlanner.ManifestFileName });

        Assert.IsNotNull(manifestRow, "manifest.json must be captured as its own Import_FileResource row");

        int linkCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Import_FileResourceBatch WHERE FileResourceId = @id;",
            new { id = (string)manifestRow!.Id });
        Assert.AreEqual(1, linkCount, "The manifest's FileResource row must be linked to the batch it drove");
    }

    /// <summary>
    /// Mirrors what <c>ISourceCacheUpdater.ResolveAsync</c> does live for every downloaded source: it
    /// rewrites the SeedFile's own FilePath to a separate download-cache directory that never contains
    /// manifest.json. Found via a T2 pass that showed manifest.json linked to only 2 of 4 bundled
    /// batches — the 2 whose files were never cache-redirected. SeedBatch.SourceDirectory (not the
    /// individual SeedFile.FilePath) must be used to find manifest.json for this to work.
    /// </summary>
    [TestMethod]
    public async Task InitialiseAsync_SeedFilePathRedirectedToCacheDir_StillFindsManifestViaSourceDirectory()
    {
        string manifestPath = Path.Combine(_sourcesDir, ManifestSeedPlanner.ManifestFileName);
        File.WriteAllText(manifestPath, """{ "files": [ { "file": "quotes.json", "name": "quotes" } ] }""");

        string cacheDir = Path.Combine(_tempDir, "download-cache");
        Directory.CreateDirectory(cacheDir);
        string cachedQuotesPath = Path.Combine(cacheDir, "quotes.json");
        File.WriteAllText(cachedQuotesPath, MinimalQuoteJson);

        SqliteFileResourceRepository fileResources = new SqliteFileResourceRepository(new SqliteConnectionFactory(_dbPath));
        // FilePath points at the cache dir (no manifest.json there) — only SourceDirectory (the real
        // scanned directory) points at _sourcesDir, matching what ISourceCacheUpdater actually produces.
        SeedBatch batch = new SeedBatch(
            [new SeedFile(cachedQuotesPath, null)], ManifestPolicy.HardcodedDefault, "test",
            SeedBatchOrigin.Bundled, SourceDirectory: _sourcesDir);

        await InitialiseAsync([batch], fileResources);

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        int manifestCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Import_FileResource WHERE FileName = @name AND IsDeleted = 0;",
            new { name = ManifestSeedPlanner.ManifestFileName });

        Assert.AreEqual(1, manifestCount, "manifest.json must still be found and captured via SeedBatch.SourceDirectory even though the SeedFile's own FilePath was cache-redirected");
    }

    [TestMethod]
    public async Task InitialiseAsync_NoManifestJsonInSourceDir_DoesNotCaptureAManifestRow()
    {
        string quotesPath = Path.Combine(_sourcesDir, "quotes.json");
        File.WriteAllText(quotesPath, MinimalQuoteJson);

        SqliteFileResourceRepository fileResources = new SqliteFileResourceRepository(new SqliteConnectionFactory(_dbPath));
        SeedBatch batch = new SeedBatch([new SeedFile(quotesPath, null)], ManifestPolicy.HardcodedDefault, "test");

        await InitialiseAsync([batch], fileResources);

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        int manifestCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Import_FileResource WHERE FileName = @name;",
            new { name = ManifestSeedPlanner.ManifestFileName });

        Assert.AreEqual(0, manifestCount);
    }

    [TestMethod]
    public async Task InitialiseAsync_SeedFileWithConverterAndOptions_CapturesThemOnTheFileResourceRow()
    {
        string quotesPath = Path.Combine(_sourcesDir, "quotes.json");
        File.WriteAllText(quotesPath, MinimalQuoteJson);

        JsonElement converterOptions = JsonDocument.Parse("""{"delimiter":","}""").RootElement;
        SeedFile seedFile = new SeedFile(quotesPath, null, Converter: "csv", ConverterOptions: converterOptions);

        SqliteFileResourceRepository fileResources = new SqliteFileResourceRepository(new SqliteConnectionFactory(_dbPath));
        SeedBatch batch = new SeedBatch([seedFile], ManifestPolicy.HardcodedDefault, "test");

        await InitialiseAsync([batch], fileResources);

        using SqliteConnection conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        dynamic row = await conn.QuerySingleAsync(
            "SELECT Converter, ConverterOptions FROM Import_FileResource WHERE FileName = 'quotes.json' AND IsDeleted = 0;");

        Assert.AreEqual("csv", (string)row.Converter);
        Assert.AreEqual("""{"delimiter":","}""", (string)row.ConverterOptions);
    }
}

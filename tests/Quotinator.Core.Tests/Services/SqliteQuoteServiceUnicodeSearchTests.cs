using Quotinator.Core.Enums;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Core.Database;
using Quotinator.Core.Entities;
using Quotinator.Core.Models;
using Quotinator.Core.Queries;
using Quotinator.Core.Services;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Import;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Core.Tests.Services;

/// <summary>
/// Proves issue #222's Unicode-aware search feature — the exact effect with and without
/// <c>Quotinator:UnicodeAwareSearch</c> active, across every affected call path, plus the
/// <c>UNICODE_CONTAINS</c> SQL function's own registration and correctness in isolation.
/// </summary>
[TestClass]
public class SqliteQuoteServiceUnicodeSearchTests
{
    private static readonly string[] DramaGenre   = ["drama"];
    private static readonly string[] FictionGenre = ["fiction"];

    private string _tempDir = null!;
    private string _dbPath  = null!;
    private string _backups = null!;
    private string _fixture = null!;

    private SqliteConnectionFactory _factory = null!;

    [TestInitialize]
    public async Task TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_unicode_search_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");
        _backups = Path.Combine(_tempDir, "backups");
        _fixture = Path.Combine(_tempDir, "unicode-search-fixture.json");

        // Two quotes chosen so every field=... variant and every fuzzy filter has a genuine
        // accented case-varying fixture to match against: "café"/"Café" (quote/source/character)
        // and "josé"/"José" (author).
        File.WriteAllText(_fixture, JsonSerializer.Serialize(new[]
        {
            new
            {
                id               = "eeeeeeee-0000-0000-0000-000000000001",
                quote            = "I will always have Café de Flore.",
                originalLanguage = "en",
                source           = "Café de Flore",
                date             = "1990",
                character        = (string?)"Amélie",
                author           = (string?)null,
                type             = "movie",
                genres           = DramaGenre,
                translations     = new { }
            },
            new
            {
                id               = "eeeeeeee-0000-0000-0000-000000000002",
                quote            = "Blindness reveals what sight conceals.",
                originalLanguage = "en",
                source           = "Blindness",
                date             = "1995",
                character        = (string?)null,
                author           = (string?)"José Saramago",
                type             = "book",
                genres           = FictionGenre,
                translations     = new { }
            },
        }));

        _factory = new SqliteConnectionFactory(_dbPath);
        DatabaseOptions options       = new DatabaseOptions { DbPath = _dbPath, BackupsPath = _backups };
        SqliteImportBatchRepository importBatches = new SqliteImportBatchRepository(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance);
        NullLogger<DatabaseInitializer> logger        = NullLogger<DatabaseInitializer>.Instance;
        SeedBatch batch         = new SeedBatch([new SeedFile(_fixture, null)], ManifestPolicy.HardcodedDefault, "unicode-search-fixture");
        ImportActionReader actionReader  = new ImportActionReader(_factory);
        ImportActionWriter actionWriter  = new ImportActionWriter(_factory);
        ImportActionResolutionCoordinator coordinator   = new ImportActionResolutionCoordinator(actionReader, actionWriter, _factory);
        SqliteImportActionService actionService = new SqliteImportActionService(actionReader, coordinator, actionWriter, NoOpAuditEntryWriter.Instance, NoOpChangeWriter.Instance,
            new SqliteRestorableRepository<QuoteEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<SourceEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<CharacterEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<PersonEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<ConversationEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<StageDirectionEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<SoundCueEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            importBatches, _factory, NoOpNotificationWriter.Instance);
        QuotinatorDatabaseInitializer db            = new QuotinatorDatabaseInitializer(_factory, options, QuotinatorMigrations.All, [batch], importBatches,
                              coordinator, actionService, actionWriter,
                              NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance, logger,
                              NoOpSourceCacheUpdater.Instance, autoUpdateSources: false,
                              autoPurgeBundledImportActions: false, autoPurgeUserImportActions: false,
                              NoOpRuleFileOverridePathResolver.Instance, NoOpSourceFileOverrideRegistry.Instance, NoOpFileResourceRepository.Instance,
                              NoOpNotificationReader.Instance, NoOpNotificationWriter.Instance, NoOpNotificationTextSource.Instance,
                              new AppVersionTracker(_factory), new VersionService(), NoOpDiskSpaceProvider.Instance);
        await db.InitialiseAsync();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private SqliteQuoteService CreateService(bool unicodeAwareSearch) => new(
        _factory,
        unicodeAwareSearch,
        new JoinQueryRepository<QuoteRow>(_factory, new QuoteLineStrategy()),
        new JoinQueryRepository<StageDirectionLineRow>(_factory, new StageDirectionLineStrategy()),
        new JoinQueryRepository<SoundCueLineRow>(_factory, new SoundCueLineStrategy()));

    // ── Canary: locks in the underlying SQLite limitation this issue exists to work around ──

    /// <summary>
    /// SQLite's own <c>LIKE</c> case-folds ASCII only — this is not our bug, it's the reason
    /// #222 exists. If SQLite's own default ever changed, this test would fail loudly.
    /// </summary>
    [TestMethod]
    public void RawSqliteLike_AccentedCharacters_IsCaseSensitive()
    {
        using SqliteConnection connection = (SqliteConnection)_factory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT 1 WHERE 'café' LIKE '%CAFÉ%';";
        object? result = command.ExecuteScalar();

        Assert.IsNull(result, "SQLite's own LIKE is documented as ASCII-only case-insensitive — " +
            "'café' should NOT match '%CAFÉ%'. If this now matches, SQLite's default behaviour " +
            "changed and #222's entire premise needs re-checking.");
    }

    // ── UNICODE_CONTAINS registration and correctness, isolated from the service layer ──

    [TestMethod]
    public void UnicodeContains_MatchesAccentedCaseVariant()
    {
        using SqliteConnection connection = (SqliteConnection)_factory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT UNICODE_CONTAINS('café', 'CAFÉ');";
        long result = (long)command.ExecuteScalar()!;

        Assert.AreEqual(1L, result);
    }

    /// <summary>
    /// Functions are registered per connection, not globally (Microsoft.Data.Sqlite) — proves the
    /// <c>SqliteConnection.StateChange</c> hook in <c>SqliteConnectionFactory</c> fires on every
    /// connection it creates, not just the first.
    /// </summary>
    [TestMethod]
    public void UnicodeContains_RegisteredOnEveryConnection()
    {
        using (SqliteConnection first = (SqliteConnection)_factory.CreateConnection())
        {
            first.Open();
            using SqliteCommand command = first.CreateCommand();
            command.CommandText = "SELECT UNICODE_CONTAINS('café', 'CAFÉ');";
            Assert.AreEqual(1L, (long)command.ExecuteScalar()!);
        }

        using (SqliteConnection second = (SqliteConnection)_factory.CreateConnection())
        {
            second.Open();
            using SqliteCommand command = second.CreateCommand();
            command.CommandText = "SELECT UNICODE_CONTAINS('café', 'CAFÉ');";
            Assert.AreEqual(1L, (long)command.ExecuteScalar()!,
                "UNICODE_CONTAINS must be registered on every connection the factory creates, not just the first.");
        }
    }

    // ── Search: same query, same fixture, only the flag differs ──

    [TestMethod]
    [DataRow("quote",     "CAFÉ",   false, false, null)]
    [DataRow("quote",     "CAFÉ",   true,  true,  null)]
    [DataRow("source",    "CAFÉ",   false, false, null)]
    [DataRow("source",    "CAFÉ",   true,  true,  "Café de Flore")]
    [DataRow("character", "AMÉLIE", false, false, null)]
    [DataRow("character", "AMÉLIE", true,  true,  "Amélie")]
    [DataRow("author",    "JOSÉ",   false, false, null)]
    [DataRow("author",    "JOSÉ",   true,  true,  "José Saramago")]
    [DataRow(null,        "CAFÉ",   false, false, null)]
    [DataRow(null,        "CAFÉ",   true,  true,  null)]
    public async Task Search_MatchesAccentedCaseVariant_OnlyWhenFlagOn(
        string? field, string query, bool unicodeAware, bool expectMatch, string? expectedFieldValue)
    {
        FilteredQuoteResult<QuoteResponse> result = await CreateService(unicodeAware).Search(query, 10, field: field);

        Assert.AreEqual(expectMatch ? FilteredResultStatus.Ok : FilteredResultStatus.NoResults, result.Status);
        if (!expectMatch) return;

        Assert.AreEqual(1, result.TotalMatching);
        if (expectedFieldValue is null) return;

        string? actual = field switch
        {
            "source"    => result.Items[0].Source,
            "character" => result.Items[0].Character,
            "author"    => result.Items[0].Author,
            _           => null,
        };
        Assert.AreEqual(expectedFieldValue, actual);
    }

    // ── GetRandom's character/author/source fuzzy filters: same shape as Search above ──

    [TestMethod]
    [DataRow("character", "AMÉLIE", false, false)]
    [DataRow("character", "AMÉLIE", true,  true)]
    [DataRow("author",    "JOSÉ",   false, false)]
    [DataRow("author",    "JOSÉ",   true,  true)]
    [DataRow("source",    "CAFÉ",   false, false)]
    [DataRow("source",    "CAFÉ",   true,  true)]
    public async Task GetRandom_FuzzyFilterMatchesAccentedCaseVariant_OnlyWhenFlagOn(
        string filter, string term, bool unicodeAware, bool expectMatch)
    {
        SqliteQuoteService service = CreateService(unicodeAware);
        FilteredQuoteResult<QuoteResponse> result = filter switch
        {
            "character" => await service.GetRandom(10, character: term),
            "author"    => await service.GetRandom(10, author: term),
            "source"    => await service.GetRandom(10, source: term),
            _           => throw new ArgumentOutOfRangeException(nameof(filter), filter, "Unknown filter"),
        };

        Assert.AreEqual(expectMatch ? FilteredResultStatus.Ok : FilteredResultStatus.NoResults, result.Status);
        if (expectMatch) Assert.AreEqual(1, result.TotalMatching);
    }
}

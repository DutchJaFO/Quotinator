using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Core.Database;
using Quotinator.Core.Entities;
using Quotinator.Core.Models;
using Quotinator.Core.Services;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Import;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Core.Tests.Data;

/// <summary>
/// Proves issue #222's Unicode-aware search feature — the exact effect with and without
/// <c>Quotinator:UnicodeAwareSearch</c> active, across every affected call path, plus the
/// <c>UNICODE_CONTAINS</c> SQL function's own registration and correctness in isolation.
/// </summary>
[TestClass]
public class SqliteQuoteServiceUnicodeSearchTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;
    private string _backups = null!;
    private string _fixture = null!;

    private IDbConnectionFactory _factory = null!;

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
                genres           = new[] { "drama" },
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
                genres           = new[] { "fiction" },
                translations     = new { }
            },
        }));

        _factory = new SqliteConnectionFactory(_dbPath);
        var options       = new DatabaseOptions { DbPath = _dbPath, BackupsPath = _backups };
        var importBatches = new SqliteImportBatchRepository(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance);
        var logger        = NullLogger<DatabaseInitializer>.Instance;
        var batch         = new SeedBatch([new SeedFile(_fixture, null)], ManifestPolicy.HardcodedDefault, "unicode-search-fixture");
        var actionReader  = new ImportActionReader(_factory);
        var actionWriter  = new ImportActionWriter(_factory);
        var coordinator   = new ImportActionResolutionCoordinator(actionReader, actionWriter, _factory);
        var actionService = new SqliteImportActionService(actionReader, coordinator, NoOpChangeWriter.Instance,
            new SqliteRestorableRepository<QuoteEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Source>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Character>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<Person>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<ConversationEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<StageDirectionEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            new SqliteRestorableRepository<SoundCueEntity>(_factory, NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance),
            importBatches, _factory);
        var db            = new QuotinatorDatabaseInitializer(_factory, options, QuotinatorMigrations.All, [batch], importBatches,
                              coordinator, actionService,
                              NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance, logger,
                              NoOpSourceCacheUpdater.Instance, autoUpdateSources: false,
                              NoOpRuleFileOverridePathResolver.Instance, NoOpSourceFileOverrideRegistry.Instance);
        await db.InitialiseAsync();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private SqliteQuoteService CreateService(bool unicodeAwareSearch) => new(_factory, unicodeAwareSearch);

    // ── Canary: locks in the underlying SQLite limitation this issue exists to work around ──

    /// <summary>
    /// SQLite's own <c>LIKE</c> case-folds ASCII only — this is not our bug, it's the reason
    /// #222 exists. If SQLite's own default ever changed, this test would fail loudly.
    /// </summary>
    [TestMethod]
    public void RawSqliteLike_AccentedCharacters_IsCaseSensitive()
    {
        using var connection = (SqliteConnection)_factory.CreateConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 WHERE 'café' LIKE '%CAFÉ%';";
        var result = command.ExecuteScalar();

        Assert.IsNull(result, "SQLite's own LIKE is documented as ASCII-only case-insensitive — " +
            "'café' should NOT match '%CAFÉ%'. If this now matches, SQLite's default behaviour " +
            "changed and #222's entire premise needs re-checking.");
    }

    // ── UNICODE_CONTAINS registration and correctness, isolated from the service layer ──

    [TestMethod]
    public void UnicodeContains_MatchesAccentedCaseVariant()
    {
        using var connection = (SqliteConnection)_factory.CreateConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT UNICODE_CONTAINS('café', 'CAFÉ');";
        var result = (long)command.ExecuteScalar()!;

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
        using (var first = (SqliteConnection)_factory.CreateConnection())
        {
            first.Open();
            using var command = first.CreateCommand();
            command.CommandText = "SELECT UNICODE_CONTAINS('café', 'CAFÉ');";
            Assert.AreEqual(1L, (long)command.ExecuteScalar()!);
        }

        using (var second = (SqliteConnection)_factory.CreateConnection())
        {
            second.Open();
            using var command = second.CreateCommand();
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
    public void Search_MatchesAccentedCaseVariant_OnlyWhenFlagOn(
        string? field, string query, bool unicodeAware, bool expectMatch, string? expectedFieldValue)
    {
        var result = CreateService(unicodeAware).Search(query, 10, field: field);

        Assert.AreEqual(expectMatch ? FilteredResultStatus.Ok : FilteredResultStatus.NoResults, result.Status);
        if (!expectMatch) return;

        Assert.AreEqual(1, result.TotalMatching);
        if (expectedFieldValue is null) return;

        var actual = field switch
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
    public void GetRandom_FuzzyFilterMatchesAccentedCaseVariant_OnlyWhenFlagOn(
        string filter, string term, bool unicodeAware, bool expectMatch)
    {
        var service = CreateService(unicodeAware);
        var result = filter switch
        {
            "character" => service.GetRandom(10, character: term),
            "author"    => service.GetRandom(10, author: term),
            "source"    => service.GetRandom(10, source: term),
            _           => throw new ArgumentOutOfRangeException(nameof(filter), filter, "Unknown filter"),
        };

        Assert.AreEqual(expectMatch ? FilteredResultStatus.Ok : FilteredResultStatus.NoResults, result.Status);
        if (expectMatch) Assert.AreEqual(1, result.TotalMatching);
    }
}

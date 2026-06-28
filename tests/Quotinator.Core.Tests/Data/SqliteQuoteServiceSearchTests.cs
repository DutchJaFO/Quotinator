using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Core.Data;
using Quotinator.Core.Models;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Import;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Data.Repositories;

namespace Quotinator.Core.Tests.Data;

/// <summary>
/// SQLite integration tests for <see cref="SqliteQuoteService.Search"/>.
/// Uses a real temp database seeded from a minimal fixture, covering the data-gap scenarios
/// identified in issue #109 (field=author and field=character with/without data).
/// </summary>
[TestClass]
public class SqliteQuoteServiceSearchTests
{
    private string _tempDir = null!;
    private string _dbPath  = null!;
    private string _backups = null!;
    private string _fixture = null!;

    private IDbConnectionFactory _factory = null!;

    [TestInitialize]
    public async Task TestInitialize()
    {
        _tempDir = Directory.CreateTempSubdirectory("quotinator_search_test_").FullName;
        _dbPath  = Path.Combine(_tempDir, "test.db");
        _backups = Path.Combine(_tempDir, "backups");
        _fixture = Path.Combine(_tempDir, "search-fixture.json");

        File.WriteAllText(_fixture, JsonSerializer.Serialize(new[]
        {
            new
            {
                id               = "ffffffff-0000-0000-0000-000000000001",
                quote            = "Surely you can't be serious.",
                originalLanguage = "en",
                source           = "Airplane!",
                date             = "1980",
                character        = (string?)"Ted Striker",
                author           = (string?)null,
                type             = "movie",
                genres           = new[] { "comedy" },
                translations     = new { }
            },
            new
            {
                id               = "ffffffff-0000-0000-0000-000000000002",
                quote            = "We shall fight on the beaches.",
                originalLanguage = "en",
                source           = "House of Commons, 4 June 1940",
                date             = "1940-06-04",
                character        = (string?)null,
                author           = (string?)"Winston Churchill",
                type             = "person",
                genres           = new[] { "non-fiction" },
                translations     = new { }
            },
            new
            {
                id               = "ffffffff-0000-0000-0000-000000000003",
                quote            = "Elementary, my dear Watson.",
                originalLanguage = "en",
                source           = "The Sherlock Holmes stories",
                date             = "1892",
                character        = (string?)null,
                author           = (string?)null,
                type             = "book",
                genres           = new[] { "mystery" },
                translations     = new { }
            },
        }));

        _factory = new SqliteConnectionFactory(_dbPath);
        var options       = new DatabaseOptions { DbPath = _dbPath, BackupsPath = _backups };
        var importBatches = new SqliteImportBatchRepository(_factory, NoOpAuditWriter.Instance, NoOpCallerContext.Instance);
        var logger        = NullLogger<DatabaseInitializer>.Instance;
        var batch         = new SeedBatch([_fixture], ManifestPolicy.HardcodedDefault, "search-fixture");
        var db            = new DatabaseInitializer(_factory, options, QuotinatorMigrations.All, [batch], importBatches,
                              NoOpAuditWriter.Instance, NoOpCallerContext.Instance, logger);
        await db.InitialiseAsync();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private SqliteQuoteService CreateService() => new(_factory);

    // ── field=quote ───────────────────────────────────────────────────────

    /// <summary>Search by quote text returns the matching quote.</summary>
    [TestMethod]
    public void Search_FieldQuote_WithMatch_ReturnsOk()
    {
        var result = CreateService().Search("serious", 10, field: "quote");

        Assert.AreEqual(FilteredResultStatus.Ok, result.Status);
        Assert.AreEqual(1, result.TotalMatching);
        StringAssert.Contains(result.Items[0].Quote, "serious");
    }

    /// <summary>Quote-field search with no match returns NoResults envelope.</summary>
    [TestMethod]
    public void Search_FieldQuote_NoMatch_ReturnsNoResults()
    {
        var result = CreateService().Search("xyzzy_no_match", 10, field: "quote");

        Assert.AreEqual(FilteredResultStatus.NoResults, result.Status);
        Assert.AreEqual(0, result.TotalMatching);
        Assert.AreEqual(0, result.Items.Count);
    }

    // ── field=source ──────────────────────────────────────────────────────

    /// <summary>Search by source title returns matching quotes.</summary>
    [TestMethod]
    public void Search_FieldSource_WithMatch_ReturnsOk()
    {
        var result = CreateService().Search("Airplane", 10, field: "source");

        Assert.AreEqual(FilteredResultStatus.Ok, result.Status);
        Assert.AreEqual(1, result.TotalMatching);
        Assert.AreEqual("Airplane!", result.Items[0].Source);
    }

    // ── field=character ───────────────────────────────────────────────────

    /// <summary>Search by character name returns the matching quote when character data exists.</summary>
    [TestMethod]
    public void Search_FieldCharacter_WithCharacterData_ReturnsOk()
    {
        var result = CreateService().Search("Striker", 10, field: "character");

        Assert.AreEqual(FilteredResultStatus.Ok, result.Status);
        Assert.AreEqual(1, result.TotalMatching);
        Assert.AreEqual("Ted Striker", result.Items[0].Character);
    }

    /// <summary>Search by character name when no quote has that character returns NoResults.</summary>
    [TestMethod]
    public void Search_FieldCharacter_NoMatch_ReturnsNoResults()
    {
        var result = CreateService().Search("Gandalf", 10, field: "character");

        Assert.AreEqual(FilteredResultStatus.NoResults, result.Status);
        Assert.AreEqual(0, result.Items.Count);
    }

    /// <summary>
    /// Search by character when quotes exist but none have a character (NULL CharacterId) returns NoResults.
    /// This is the data-gap scenario from issue #109: NULL LIKE '%x%' is NULL, not TRUE.
    /// </summary>
    [TestMethod]
    public void Search_FieldCharacter_QuoteHasNoCharacter_ReturnsNoResults()
    {
        // The "We shall fight" and "Elementary" quotes have no character — searching for anything
        // via field=character should not match them even if the term appears elsewhere.
        var result = CreateService().Search("Churchill", 10, field: "character");

        Assert.AreEqual(FilteredResultStatus.NoResults, result.Status);
        Assert.AreEqual(0, result.Items.Count);
    }

    // ── field=author ──────────────────────────────────────────────────────

    /// <summary>Search by author name returns the matching quote when author data exists.</summary>
    [TestMethod]
    public void Search_FieldAuthor_WithAuthorData_ReturnsOk()
    {
        var result = CreateService().Search("Churchill", 10, field: "author");

        Assert.AreEqual(FilteredResultStatus.Ok, result.Status);
        Assert.AreEqual(1, result.TotalMatching);
        Assert.AreEqual("Winston Churchill", result.Items[0].Author);
    }

    /// <summary>Search by author name when no quote has that author returns NoResults.</summary>
    [TestMethod]
    public void Search_FieldAuthor_NoMatch_ReturnsNoResults()
    {
        var result = CreateService().Search("Tolkien", 10, field: "author");

        Assert.AreEqual(FilteredResultStatus.NoResults, result.Status);
        Assert.AreEqual(0, result.Items.Count);
    }

    /// <summary>
    /// Search by author when quotes exist but none have an author (NULL PersonId) returns NoResults.
    /// This is the data-gap scenario from issue #109: the bundled sources produce 0 People rows.
    /// </summary>
    [TestMethod]
    public void Search_FieldAuthor_QuoteHasNoAuthor_ReturnsNoResults()
    {
        // "Surely you can't be serious" has no author — searching any term via field=author
        // must not match it even if the term appears in the quote text.
        var result = CreateService().Search("serious", 10, field: "author");

        Assert.AreEqual(FilteredResultStatus.NoResults, result.Status);
        Assert.AreEqual(0, result.Items.Count);
    }

    // ── type filter ───────────────────────────────────────────────────────

    /// <summary>type=person returns only person-type quotes.</summary>
    [TestMethod]
    public void Search_TypePerson_ReturnsPerson()
    {
        var result = CreateService().Search("fight", 10, types: ["person"]);

        Assert.AreEqual(FilteredResultStatus.Ok, result.Status);
        Assert.AreEqual(1, result.TotalMatching);
        Assert.AreEqual("person", result.Items[0].Type);
    }

    /// <summary>type=anime with no anime quotes in the dataset returns NoResults.</summary>
    [TestMethod]
    public void Search_TypeAnime_NoAnimeData_ReturnsNoResults()
    {
        var result = CreateService().Search("the", 10, types: ["anime"]);

        Assert.AreEqual(FilteredResultStatus.NoResults, result.Status);
        Assert.AreEqual(0, result.Items.Count);
    }

    // ── default (all fields) ──────────────────────────────────────────────

    /// <summary>Default search (no field) matches across quote text and source.</summary>
    [TestMethod]
    public void Search_AllFields_MatchesAcrossQuoteAndSource()
    {
        var result = CreateService().Search("Airplane", 10);

        Assert.AreEqual(FilteredResultStatus.Ok, result.Status);
        Assert.AreEqual(1, result.TotalMatching);
        Assert.AreEqual("Airplane!", result.Items[0].Source);
    }

    /// <summary>limit caps the result count.</summary>
    [TestMethod]
    public void Search_LimitCapsResults()
    {
        // All 3 quotes in the fixture match "the" somewhere — limit to 2
        var result = CreateService().Search("e", 2);

        Assert.AreEqual(FilteredResultStatus.Ok, result.Status);
        Assert.AreEqual(2, result.TotalMatching);
        Assert.AreEqual(2, result.Items.Count);
    }
}

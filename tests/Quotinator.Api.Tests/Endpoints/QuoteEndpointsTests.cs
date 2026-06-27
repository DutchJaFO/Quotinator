using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Core.Services;
using Quotinator.Data.Database;

namespace Quotinator.Api.Tests.Endpoints;

[TestClass]
public class QuoteEndpointsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(new NoOpDatabaseInitializer());
            }));

    // ── /random — envelope shape ──────────────────────────────────────────

    /// <summary>GET /random returns the FilteredQuoteResult envelope with status=Ok.</summary>
    [TestMethod]
    public async Task GetRandom_NoN_ReturnsEnvelopeWithSingleItem()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/random");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("Ok", doc.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(JsonValueKind.Array, doc.RootElement.GetProperty("items").ValueKind);
        Assert.AreEqual(1, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.IsTrue(doc.RootElement.GetProperty("totalMatching").GetInt32() > 0);
    }

    /// <summary>GET /random?n=2 returns an envelope with 2 items.</summary>
    [TestMethod]
    public async Task GetRandom_WithN_ReturnsEnvelopeWithNItems()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/random?n=2");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("Ok", doc.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(2, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    /// <summary>n=0 is rejected with 400.</summary>
    [TestMethod]
    public async Task GetRandom_NZero_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/random?n=0");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>n=101 is rejected with 400.</summary>
    [TestMethod]
    public async Task GetRandom_NTooLarge_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/random?n=101");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>A non-integer n is rejected with 400, not 500.</summary>
    [TestMethod]
    public async Task GetRandom_NNotInteger_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/random?n=g");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(doc.RootElement.TryGetProperty("detail", out _));
    }

    // ── /random — type/genre multi-value filters ──────────────────────────

    /// <summary>type=movie filters the pool to movie quotes.</summary>
    [TestMethod]
    public async Task GetRandom_TypeFilter_ReturnsOnlyMatchingType()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/random?n=10&type=movie");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("Ok", doc.RootElement.GetProperty("status").GetString());
        foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
            Assert.AreEqual("movie", item.GetProperty("type").GetString());
    }

    /// <summary>type=movie and type=book filters with OR logic.</summary>
    [TestMethod]
    public async Task GetRandom_MultipleTypes_AppliesOrLogic()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/random?n=10&type=movie&type=book");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("Ok", doc.RootElement.GetProperty("status").GetString());
        foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            var t = item.GetProperty("type").GetString();
            Assert.IsTrue(t == "movie" || t == "book", $"Unexpected type: {t}");
        }
    }

    /// <summary>genre=fantasy and genre=sci-fi filters with OR logic.</summary>
    [TestMethod]
    public async Task GetRandom_MultipleGenres_AppliesOrLogic()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/random?n=10&genre=fantasy&genre=sci-fi");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("Ok", doc.RootElement.GetProperty("status").GetString());
        Assert.IsGreaterThan(0, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    /// <summary>totalMatching reflects the pool size, not n.</summary>
    [TestMethod]
    public async Task GetRandom_TotalMatchingReflectsPool()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/random?n=1&type=movie");

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        // FakeQuoteService has 2 movie quotes; requesting n=1 should show totalMatching=2
        Assert.AreEqual(1, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.AreEqual(2, doc.RootElement.GetProperty("totalMatching").GetInt32());
    }

    // ── /random — text filters ────────────────────────────────────────────

    /// <summary>character=Rick filters to quotes by that character.</summary>
    [TestMethod]
    public async Task GetRandom_CharacterFilter_ReturnsMatchingQuotes()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/random?n=10&character=Rick");

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("Ok", doc.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(1, doc.RootElement.GetProperty("items").GetArrayLength());
        StringAssert.Contains(doc.RootElement.GetProperty("items")[0].GetProperty("character").GetString(), "Rick");
    }

    /// <summary>author=Churchill filters to quotes by that author.</summary>
    [TestMethod]
    public async Task GetRandom_AuthorFilter_ReturnsMatchingQuotes()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/random?n=10&author=Churchill");

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("Ok", doc.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(1, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    /// <summary>source=Casablanca filters to quotes from that source.</summary>
    [TestMethod]
    public async Task GetRandom_SourceFilter_ReturnsMatchingQuotes()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/random?n=10&source=Casablanca");

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("Ok", doc.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(1, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    // ── /random — filter validation in envelope ───────────────────────────

    /// <summary>Unknown type value returns 422 envelope with status=InvalidType.</summary>
    [TestMethod]
    public async Task GetRandom_UnknownType_Returns422WithInvalidTypeEnvelope()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/random?type=cartoon");

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("InvalidType", doc.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(0, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.IsFalse(string.IsNullOrEmpty(doc.RootElement.GetProperty("message").GetString()));
    }

    /// <summary>Unknown genre value returns 422 envelope with status=InvalidGenre.</summary>
    [TestMethod]
    public async Task GetRandom_UnknownGenre_Returns422WithInvalidGenreEnvelope()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/random?genre=notarealgenre");

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("InvalidGenre", doc.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(0, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    /// <summary>Character filter longer than 200 chars returns 400 envelope with status=InputTooLong.</summary>
    [TestMethod]
    public async Task GetRandom_CharacterTooLong_Returns400WithInputTooLongEnvelope()
    {
        using var factory = CreateFactory();
        var longValue = new string('a', 201);
        var response = await factory.CreateClient().GetAsync($"/api/v1/quotes/random?character={longValue}");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("InputTooLong", doc.RootElement.GetProperty("status").GetString());
    }

    /// <summary>Suspicious input in a text filter returns 400 envelope with status=InvalidInput.</summary>
    [TestMethod]
    public async Task GetRandom_SuspiciousCharacterInput_Returns400WithInvalidInputEnvelope()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/random?character=%27%20OR%201%3D1%20--");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("InvalidInput", doc.RootElement.GetProperty("status").GetString());
    }

    /// <summary>Valid type filter with no matching quotes returns envelope with status=NoResults.</summary>
    [TestMethod]
    public async Task GetRandom_ValidFilterNoMatches_ReturnsNoResultsEnvelope()
    {
        using var factory = CreateFactory();
        // FakeQuoteService has no anime quotes
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/random?type=anime");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("NoResults", doc.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(0, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.IsFalse(string.IsNullOrEmpty(doc.RootElement.GetProperty("message").GetString()));
    }

    // ── /{id} ─────────────────────────────────────────────────────────────

    /// <summary>Known ID returns the correct quote.</summary>
    [TestMethod]
    public async Task GetById_KnownId_ReturnsQuote()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient()
            .GetAsync($"/api/v1/quotes/{FakeQuoteService.CasablancaEn.Id}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(FakeQuoteService.CasablancaEn.Id, doc.RootElement.GetProperty("id").GetString());
    }

    /// <summary>Unknown ID returns 404.</summary>
    [TestMethod]
    public async Task GetById_UnknownId_ReturnsNotFound()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/does-not-exist");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>?lang=nl returns the Dutch translation and isTranslated=true.</summary>
    [TestMethod]
    public async Task GetById_WithLang_ReturnsTranslation()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient()
            .GetAsync($"/api/v1/quotes/{FakeQuoteService.CasablancaEn.Id}?lang=nl");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("nl", doc.RootElement.GetProperty("language").GetString());
        Assert.IsTrue(doc.RootElement.GetProperty("isTranslated").GetBoolean());
    }

    // ── /search ───────────────────────────────────────────────────────────

    /// <summary>Matching query returns results in the envelope with status=Ok.</summary>
    [TestMethod]
    public async Task Search_MatchingQuery_ReturnsOkEnvelope()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/search?q=looking");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual("Ok", doc.GetProperty("status").GetString());
        Assert.AreEqual(JsonValueKind.Array, doc.GetProperty("items").ValueKind);
        Assert.IsGreaterThan(0, doc.GetProperty("items").GetArrayLength());
        Assert.IsTrue(doc.TryGetProperty("totalMatching", out _));
    }

    /// <summary>No matching query returns envelope with status=NoResults and a message.</summary>
    [TestMethod]
    public async Task Search_NoResults_ReturnsNoResultsEnvelope()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/search?q=xyzzy_no_match_ever");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual("NoResults", doc.GetProperty("status").GetString());
        Assert.AreEqual(0, doc.GetProperty("items").GetArrayLength());
        Assert.IsFalse(string.IsNullOrEmpty(doc.GetProperty("message").GetString()));
    }

    /// <summary>Unknown type value returns 422 envelope with status=InvalidType.</summary>
    [TestMethod]
    public async Task Search_UnknownType_Returns422WithInvalidTypeEnvelope()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/search?q=the&type=cartoon");

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual("InvalidType", doc.GetProperty("status").GetString());
        Assert.AreEqual(0, doc.GetProperty("items").GetArrayLength());
        Assert.IsFalse(string.IsNullOrEmpty(doc.GetProperty("message").GetString()));
    }

    /// <summary>Unknown genre value returns 422 envelope with status=InvalidGenre.</summary>
    [TestMethod]
    public async Task Search_UnknownGenre_Returns422WithInvalidGenreEnvelope()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/search?q=the&genre=notarealgenre");

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual("InvalidGenre", doc.GetProperty("status").GetString());
        Assert.AreEqual(0, doc.GetProperty("items").GetArrayLength());
    }

    /// <summary>Empty q is rejected with 400.</summary>
    [TestMethod]
    public async Task Search_EmptyQuery_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/search?q=");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Missing q is rejected with 400.</summary>
    [TestMethod]
    public async Task Search_MissingQuery_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/search");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Missing q returns a ProblemDetails body with a detail field.</summary>
    [TestMethod]
    public async Task Search_MissingQuery_ReturnsProblemDetails()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/search");
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(doc.RootElement.TryGetProperty("detail", out var detail));
        Assert.IsFalse(string.IsNullOrWhiteSpace(detail.GetString()));
    }

    /// <summary>Missing q with Accept-Language: nl returns a Dutch ProblemDetails detail.</summary>
    [TestMethod]
    public async Task Search_MissingQuery_WithAcceptLanguage_ReturnsLocalisedError()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("nl");
        var response = await client.GetAsync("/api/v1/quotes/search");
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(doc.RootElement.TryGetProperty("detail", out var detail));
        StringAssert.Contains(detail.GetString(), "verplicht");
    }

    // ── search field filter ───────────────────────────────────────────────

    /// <summary>field=quote matches on quote text only.</summary>
    [TestMethod]
    public async Task Search_FieldQuote_MatchesQuoteText()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/search?q=back&field=quote");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual("Ok", doc.GetProperty("status").GetString());
        var items = doc.GetProperty("items");
        Assert.AreEqual(1, items.GetArrayLength());
        StringAssert.Contains(items[0].GetProperty("quote").GetString(), "back");
    }

    /// <summary>field=source matches on source only, not quote text.</summary>
    [TestMethod]
    public async Task Search_FieldSource_MatchesSourceOnly()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/search?q=Casablanca&field=source");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual("Ok", doc.GetProperty("status").GetString());
        var items = doc.GetProperty("items");
        Assert.AreEqual(1, items.GetArrayLength());
        Assert.AreEqual("Casablanca", items[0].GetProperty("source").GetString());
    }

    /// <summary>field=character matches on character name.</summary>
    [TestMethod]
    public async Task Search_FieldCharacter_MatchesCharacter()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/search?q=Rick&field=character");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual("Ok", doc.GetProperty("status").GetString());
        var items = doc.GetProperty("items");
        Assert.AreEqual(1, items.GetArrayLength());
        Assert.AreEqual("Rick Blaine", items[0].GetProperty("character").GetString());
    }

    /// <summary>field=author matches on author name.</summary>
    [TestMethod]
    public async Task Search_FieldAuthor_MatchesAuthor()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/search?q=Churchill&field=author");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual("Ok", doc.GetProperty("status").GetString());
        var items = doc.GetProperty("items");
        Assert.AreEqual(1, items.GetArrayLength());
        Assert.AreEqual("Winston Churchill", items[0].GetProperty("author").GetString());
    }

    /// <summary>An invalid field value is rejected with 400.</summary>
    [TestMethod]
    public async Task Search_InvalidField_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/search?q=test&field=invalid");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── search multi-value filters ────────────────────────────────────────

    /// <summary>type=movie and type=person on /search applies OR logic.</summary>
    [TestMethod]
    public async Task Search_MultipleTypes_AppliesOrLogic()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/search?q=the&type=movie&type=person");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual("Ok", doc.GetProperty("status").GetString());
        foreach (var item in doc.GetProperty("items").EnumerateArray())
        {
            var t = item.GetProperty("type").GetString();
            Assert.IsTrue(t == "movie" || t == "person", $"Unexpected type: {t}");
        }
    }

    // ── input validation (shared) ─────────────────────────────────────────

    /// <summary>An invalid lang value is rejected with 400.</summary>
    [TestMethod]
    public async Task GetRandom_InvalidLang_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/random?lang=not-a-real-lang-code-xyz");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>A search term exceeding 200 characters is rejected with 400.</summary>
    [TestMethod]
    public async Task Search_QueryTooLong_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        var longQuery = new string('a', 201);
        var response = await factory.CreateClient().GetAsync($"/api/v1/quotes/search?q={longQuery}");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── / (paginated list) ────────────────────────────────────────────────

    /// <summary>Default list returns paginated result with expected shape.</summary>
    [TestMethod]
    public async Task GetAll_Default_ReturnsPaginatedResult()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(doc.RootElement.TryGetProperty("items", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("totalCount", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("totalPages", out _));
    }

    /// <summary>page=0 is rejected with 400.</summary>
    [TestMethod]
    public async Task GetAll_PageZero_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes?page=0");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>?type=person filters to person quotes only.</summary>
    [TestMethod]
    public async Task GetAll_TypeFilter_ReturnsFilteredResults()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes?type=person");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
            Assert.AreEqual("person", item.GetProperty("type").GetString());
    }

    /// <summary>type=movie and type=book on / applies OR logic.</summary>
    [TestMethod]
    public async Task GetAll_MultipleTypes_AppliesOrLogic()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes?type=movie&type=book");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            var t = item.GetProperty("type").GetString();
            Assert.IsTrue(t == "movie" || t == "book", $"Unexpected type: {t}");
        }
    }

    /// <summary>Unknown type on / returns 422 envelope with status=InvalidType.</summary>
    [TestMethod]
    public async Task GetAll_UnknownType_Returns422WithInvalidTypeEnvelope()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes?type=cartoon");

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("InvalidType", doc.RootElement.GetProperty("status").GetString());
    }

    /// <summary>Unknown genre on / returns 422 envelope with status=InvalidGenre.</summary>
    [TestMethod]
    public async Task GetAll_UnknownGenre_Returns422WithInvalidGenreEnvelope()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes?genre=notarealgenre");

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("InvalidGenre", doc.RootElement.GetProperty("status").GetString());
    }

    // ── /random — year/decade filters ────────────────────────────────────

    /// <summary>yearFrom=1980 excludes Casablanca (1942), Churchill (1940), and Tolkien (1954).</summary>
    [TestMethod]
    public async Task GetRandom_YearFrom_ExcludesOlderQuotes()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/random?n=10&yearFrom=1980");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("Ok", doc.RootElement.GetProperty("status").GetString());
        // Only Terminator (1984) should match
        Assert.AreEqual(1, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.AreEqual(FakeQuoteService.Terminator.Id, doc.RootElement.GetProperty("items")[0].GetProperty("id").GetString());
    }

    /// <summary>yearTo=1942 matches Casablanca (1942) and Churchill (1940) but not Tolkien (1954) or Terminator (1984).</summary>
    [TestMethod]
    public async Task GetRandom_YearTo_ExcludesNewerQuotes()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/random?n=10&yearTo=1942");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("Ok", doc.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(2, doc.RootElement.GetProperty("totalMatching").GetInt32());
    }

    /// <summary>year=1942 shorthand expands to yearFrom=1942 and yearTo=1942 — only Casablanca matches.</summary>
    [TestMethod]
    public async Task GetRandom_YearShorthand_MatchesExactYear()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/random?n=10&year=1942");

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("Ok", doc.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(1, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.AreEqual(FakeQuoteService.CasablancaEn.Id, doc.RootElement.GetProperty("items")[0].GetProperty("id").GetString());
    }

    /// <summary>decade=1940 expands to 1940–1949 — Casablanca (1942) and Churchill (1940) match.</summary>
    [TestMethod]
    public async Task GetRandom_DecadeShorthand_MatchesQuotesInRange()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/random?n=10&decade=1940");

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("Ok", doc.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(2, doc.RootElement.GetProperty("totalMatching").GetInt32());
    }

    /// <summary>decade not divisible by 10 returns 422 envelope with status=InvalidInput.</summary>
    [TestMethod]
    public async Task GetRandom_DecadeNotDivisibleByTen_Returns422WithInvalidInputEnvelope()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/random?decade=1941");

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("InvalidInput", doc.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(0, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    /// <summary>yearFrom greater than yearTo returns 422 envelope with status=InvalidInput.</summary>
    [TestMethod]
    public async Task GetRandom_YearFromGreaterThanYearTo_Returns422WithInvalidInputEnvelope()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/random?yearFrom=1984&yearTo=1942");

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("InvalidInput", doc.RootElement.GetProperty("status").GetString());
    }

    /// <summary>yearFrom greater than yearTo on /search returns 422.</summary>
    [TestMethod]
    public async Task Search_YearFromGreaterThanYearTo_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/search?q=x&yearFrom=1984&yearTo=1942");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    /// <summary>yearFrom greater than yearTo on / returns 422.</summary>
    [TestMethod]
    public async Task GetAll_YearFromGreaterThanYearTo_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes?yearFrom=1984&yearTo=1942");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    /// <summary>An invalid lang value on /search is rejected with 400.</summary>
    [TestMethod]
    public async Task Search_InvalidLang_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/search?q=x&lang=not-a-real-lang-code-xyz");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>An invalid lang value on / is rejected with 400.</summary>
    [TestMethod]
    public async Task GetAll_InvalidLang_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes?lang=not-a-real-lang-code-xyz");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── / (paginated list) — year/decade filters ─────────────────────────

    /// <summary>yearFrom=1980 on / filters to only Terminator (1984).</summary>
    [TestMethod]
    public async Task GetAll_YearFrom_FiltersOlderQuotes()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes?yearFrom=1980");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("items");
        Assert.AreEqual(1, items.GetArrayLength());
        Assert.AreEqual(FakeQuoteService.Terminator.Id, items[0].GetProperty("id").GetString());
    }

    /// <summary>decade not divisible by 10 on / returns 422 — semantic error, same as /random.</summary>
    [TestMethod]
    public async Task GetAll_DecadeNotDivisibleByTen_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes?decade=1941");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── /search — year/decade filters ────────────────────────────────────

    /// <summary>year=1984 on /search returns only Terminator.</summary>
    [TestMethod]
    public async Task Search_YearShorthand_FiltersResults()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/search?q=back&year=1984");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual("Ok", doc.GetProperty("status").GetString());
        var items = doc.GetProperty("items");
        Assert.AreEqual(1, items.GetArrayLength());
        Assert.AreEqual(FakeQuoteService.Terminator.Id, items[0].GetProperty("id").GetString());
    }

    /// <summary>decade not divisible by 10 on /search returns 422 — semantic error, same as /random.</summary>
    [TestMethod]
    public async Task Search_DecadeNotDivisibleByTen_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/search?q=the&decade=1941");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── parameter binding errors ──────────────────────────────────────────

    /// <summary>Non-integer yearTo returns 422 — wrong value type is a semantic error.</summary>
    [TestMethod]
    public async Task Search_NonIntegerYearTo_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient()
            .GetAsync("/api/v1/quotes/search?q=x&yearFrom=1980&yearTo=1981x");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    /// <summary>Non-integer yearFrom returns 422 — wrong value type is a semantic error.</summary>
    [TestMethod]
    public async Task GetRandom_NonIntegerYearFrom_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient()
            .GetAsync("/api/v1/quotes/random?yearFrom=abc");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    /// <summary>Non-integer decade returns 422 — wrong value type is a semantic error.</summary>
    [TestMethod]
    public async Task GetAll_NonIntegerDecade_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient()
            .GetAsync("/api/v1/quotes?decade=1980x");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    /// <summary>Error detail names the specific parameter that failed — not a generic list of all params.</summary>
    [TestMethod]
    public async Task GetRandom_NonIntegerYearFrom_DetailNamesParameter()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/random?yearFrom=abc");
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "yearFrom");
        Assert.IsFalse(body.Contains("pageSize"), "Detail must not list unrelated parameters");
    }

    /// <summary>Error detail names yearTo specifically — not a generic list of all params.</summary>
    [TestMethod]
    public async Task Search_NonIntegerYearTo_DetailNamesParameter()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/search?q=x&yearTo=1981x");
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "yearTo");
        Assert.IsFalse(body.Contains("pageSize"), "Detail must not list unrelated parameters");
    }

    /// <summary>Error detail names decade specifically — not a generic list of all params.</summary>
    [TestMethod]
    public async Task GetAll_NonIntegerDecade_DetailNamesParameter()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes?decade=1980x");
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "decade");
        Assert.IsFalse(body.Contains("pageSize"), "Detail must not list unrelated parameters");
    }
}

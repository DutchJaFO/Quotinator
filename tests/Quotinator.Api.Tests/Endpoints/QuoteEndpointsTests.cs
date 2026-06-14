using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Core.Services;

namespace Quotinator.Api.Tests.Endpoints;

[TestClass]
public class QuoteEndpointsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddSingleton<IQuoteService>(new FakeQuoteService())));

    // ── /random ──────────────────────────────────────────────────────────

    /// <summary>GET /random with no n returns a single quote object.</summary>
    [TestMethod]
    public async Task GetRandom_NoN_ReturnsSingleQuote()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/random");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(doc.RootElement.TryGetProperty("id", out _));
    }

    /// <summary>GET /random?n=2 returns an array of 2 quotes.</summary>
    [TestMethod]
    public async Task GetRandom_WithN_ReturnsArray()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/random?n=2");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var items = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual(JsonValueKind.Array, items.ValueKind);
        Assert.AreEqual(2, items.GetArrayLength());
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

    /// <summary>Matching query returns results.</summary>
    [TestMethod]
    public async Task Search_MatchingQuery_ReturnsResults()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes/search?q=looking");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var items = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual(JsonValueKind.Array, items.ValueKind);
        Assert.IsGreaterThan(0, items.GetArrayLength());
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
        // Verify the response is Dutch, not English
        StringAssert.Contains(detail.GetString(), "verplicht");
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

    /// <summary>An unknown type value is rejected with 400.</summary>
    [TestMethod]
    public async Task GetAll_InvalidType_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/quotes?type=invalid");
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
}

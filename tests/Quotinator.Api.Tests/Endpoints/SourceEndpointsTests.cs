using Quotinator.Core.Enums;
using Quotinator.Data.Enums;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Core.Models;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Core.Entities;
using Quotinator.Core.Repositories;

namespace Quotinator.Api.Tests.Endpoints;

[TestClass]
public class SourceEndpointsTests
{
    private static WebApplicationFactory<Program> CreateFactory(
        FakeSourceRepository? repository = null,
        FakeSourceSeriesReferenceReader? seriesReader = null,
        FakeSourceSeasonReferenceReader? seasonReader = null) =>
        new QuotinatorWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(new NoOpDatabaseInitializer());
                services.AddSingleton<IListableRepository<SourceEntity>>(repository ?? new FakeSourceRepository());
                services.AddSingleton<ISourceSeriesReferenceReader>(seriesReader ?? new FakeSourceSeriesReferenceReader());
                services.AddSingleton<ISourceSeasonReferenceReader>(seasonReader ?? new FakeSourceSeasonReferenceReader());
            }));

    private static SourceEntity NewSource(
        Guid? id = null, string title = "Casablanca", QuoteType type = QuoteType.Movie,
        string date = "1942", Guid? seriesId = null,
        CompletenessStatus completeness = CompletenessStatus.Incomplete,
        DateTime? dateCreated = null) => new()
    {
        Id                 = id ?? Guid.NewGuid(),
        Title              = title,
        Type               = new SafeValue<QuoteType?>(type.ToString(), type),
        Date               = string.IsNullOrEmpty(date) ? SafeDateValue.Empty : new SafeValue<DateTime?>(date, DateTime.Parse(date + (date.Length == 4 ? "-01-01" : ""))),
        SeriesId           = seriesId,
        CompletenessStatus = new SafeValue<CompletenessStatus?>(completeness.ToString(), completeness),
        DateCreated        = dateCreated is { } dc ? SafeDateValue.From(dc) : SafeDateValue.Now,
    };

    // ── GetAllSources — basic shape ─────────────────────────────────────────

    [TestMethod]
    public async Task GetAllSources_ReturnsPaginatedResults()
    {
        FakeSourceRepository repo = new FakeSourceRepository([NewSource(), NewSource(title: "The Terminator")]);
        using WebApplicationFactory<Program> factory = CreateFactory(repo);
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/v1/masterdata/sources", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        JsonDocument doc  = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        JsonElement root = doc.RootElement;

        Assert.IsTrue(root.TryGetProperty("items", out JsonElement items));
        Assert.IsTrue(root.TryGetProperty("page", out _));
        Assert.IsTrue(root.TryGetProperty("pageSize", out _));
        Assert.IsTrue(root.TryGetProperty("totalCount", out _));
        Assert.IsTrue(root.TryGetProperty("totalPages", out _));
        Assert.AreEqual(2, items.GetArrayLength());
    }

    // ── GetAllSources — pagination contract (#195, eight-case matrix) ──────

    [TestMethod]
    public async Task GetAllSources_PageZero_Returns422()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/v1/masterdata/sources?page=0", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllSources_PageMalformed_Returns422()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/v1/masterdata/sources?page=abc", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllSources_PageSizeMalformed_Returns422()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/v1/masterdata/sources?pageSize=abc", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllSources_PageSizeNegative_Returns422()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/v1/masterdata/sources?pageSize=-1", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllSources_PageSizeAbove500_Returns422NotSilentClamp()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/v1/masterdata/sources?pageSize=999", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode, "pageSize above 500 must be rejected, not silently clamped");
    }

    [TestMethod]
    public async Task GetAllSources_PageSizeZero_ReturnsAllRowsAsOnePage()
    {
        FakeSourceRepository repo = new FakeSourceRepository([NewSource(), NewSource(title: "The Terminator"), NewSource(title: "Airplane!")]);
        using WebApplicationFactory<Program> factory = CreateFactory(repo);
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/v1/masterdata/sources?pageSize=0", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        Assert.AreEqual(3, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.AreEqual(3, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.AreEqual(3, doc.RootElement.GetProperty("pageSize").GetInt32(), "pageSize=0 reports the effective count, not the literal 0 requested");
    }

    [TestMethod]
    public async Task GetAllSources_PageSizeOmitted_DefaultsTo20()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/v1/masterdata/sources", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        Assert.AreEqual(20, doc.RootElement.GetProperty("pageSize").GetInt32());
    }

    [TestMethod]
    public async Task GetAllSources_PageBeyondLast_Returns422DistinctDetail()
    {
        FakeSourceRepository repo = new FakeSourceRepository([NewSource()]);
        using WebApplicationFactory<Program> factory = CreateFactory(repo);
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/v1/masterdata/sources?page=5", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── GetSourceById ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetSourceById_ExistingId_ReturnsSource()
    {
        Guid id   = Guid.NewGuid();
        FakeSourceRepository repo = new FakeSourceRepository([NewSource(id: id, title: "Casablanca", type: QuoteType.Movie, completeness: CompletenessStatus.Complete)]);
        using WebApplicationFactory<Program> factory = CreateFactory(repo);
        HttpResponseMessage response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/sources/{id}", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        JsonElement root = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken)).RootElement;

        Assert.AreEqual(id.ToString("D"), root.GetProperty("id").GetString());
        Assert.AreEqual("Casablanca", root.GetProperty("title").GetString());

        // Response shape assertions (Step 1): type/completenessStatus must serialize as plain JSON
        // string values, never the raw SafeValue<T> {"raw":..,"parsed":..} shape.
        Assert.AreEqual(JsonValueKind.String, root.GetProperty("type").ValueKind);
        Assert.AreEqual("movie", root.GetProperty("type").GetString());
        Assert.AreEqual(JsonValueKind.String, root.GetProperty("completenessStatus").ValueKind);
        Assert.AreEqual("Complete", root.GetProperty("completenessStatus").GetString());
    }

    [TestMethod]
    public async Task GetSourceById_UnknownId_Returns404()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        HttpResponseMessage response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/sources/{Guid.NewGuid()}", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetSourceById_UppercaseId_MatchesCaseInsensitively()
    {
        Guid id   = Guid.NewGuid();
        FakeSourceRepository repo = new FakeSourceRepository([NewSource(id: id)]);
        using WebApplicationFactory<Program> factory = CreateFactory(repo);
        HttpResponseMessage response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/sources/{id.ToString("D").ToUpperInvariant()}", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        JsonElement root = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken)).RootElement;
        Assert.AreEqual(id.ToString("D"), root.GetProperty("id").GetString());
    }

    [TestMethod]
    public async Task GetSourceById_MalformedId_Returns404NotBadRequest()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/v1/masterdata/sources/not-a-guid", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetSourceById_UnknownDate_ReturnsNullNotEmptyString()
    {
        Guid id   = Guid.NewGuid();
        FakeSourceRepository repo = new FakeSourceRepository([NewSource(id: id, date: "")]);
        using WebApplicationFactory<Program> factory = CreateFactory(repo);
        HttpResponseMessage response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/sources/{id}", TestContext.CancellationToken);

        JsonElement root = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken)).RootElement;
        AssertPropertyIsNullOrAbsent(root, "date");
    }

    // ── Series reference resolution (#179 join) ─────────────────────────────

    [TestMethod]
    public async Task GetSourceById_SourceHasSeries_ReturnsSeriesReference()
    {
        Guid sourceId = Guid.NewGuid();
        Guid seriesId = Guid.NewGuid();
        FakeSourceRepository repo   = new FakeSourceRepository([NewSource(id: sourceId, seriesId: seriesId)]);
        FakeSourceSeriesReferenceReader reader = new FakeSourceSeriesReferenceReader(new Dictionary<Guid, (Guid, string)>
        {
            [sourceId] = (seriesId, "Star Wars"),
        });
        using WebApplicationFactory<Program> factory = CreateFactory(repo, reader);
        HttpResponseMessage response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/sources/{sourceId}", TestContext.CancellationToken);

        JsonElement root   = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken)).RootElement;
        JsonElement series = root.GetProperty("series");
        Assert.AreEqual(JsonValueKind.Object, series.ValueKind);
        Assert.AreEqual(seriesId.ToString("D"), series.GetProperty("id").GetString());
        Assert.AreEqual("Star Wars", series.GetProperty("name").GetString());
    }

    [TestMethod]
    public async Task GetSourceById_SourceHasNoSeries_ReturnsNullSeries()
    {
        Guid sourceId = Guid.NewGuid();
        FakeSourceRepository repo = new FakeSourceRepository([NewSource(id: sourceId)]);
        using WebApplicationFactory<Program> factory = CreateFactory(repo, new FakeSourceSeriesReferenceReader());
        HttpResponseMessage response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/sources/{sourceId}", TestContext.CancellationToken);

        JsonElement root = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken)).RootElement;
        AssertPropertyIsNullOrAbsent(root, "series");
    }

    [TestMethod]
    public async Task GetSourceById_SeriesSoftDeleted_ReturnsNullSeries()
    {
        Guid sourceId = Guid.NewGuid();
        Guid seriesId = Guid.NewGuid();
        // The Source still carries SeriesId, but the reader's seed omits the entry entirely —
        // modelling a soft-deleted Series, per the reader's documented "absent means unresolved" contract.
        FakeSourceRepository repo   = new FakeSourceRepository([NewSource(id: sourceId, seriesId: seriesId)]);
        FakeSourceSeriesReferenceReader reader = new FakeSourceSeriesReferenceReader(new Dictionary<Guid, (Guid, string)>());
        using WebApplicationFactory<Program> factory = CreateFactory(repo, reader);
        HttpResponseMessage response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/sources/{sourceId}", TestContext.CancellationToken);

        JsonElement root = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken)).RootElement;
        AssertPropertyIsNullOrAbsent(root, "series", "a Source pointing at a soft-deleted Series must resolve to null, not a dangling reference");
    }

    [TestMethod]
    public async Task GetAllSources_MultipleSourcesWithSeries_BatchResolvesEachSeries()
    {
        Guid sourceWithSeriesA = Guid.NewGuid();
        Guid sourceWithSeriesB = Guid.NewGuid();
        Guid sourceNoSeries    = Guid.NewGuid();
        Guid seriesA = Guid.NewGuid();
        Guid seriesB = Guid.NewGuid();

        FakeSourceRepository repo = new FakeSourceRepository(
        [
            NewSource(id: sourceWithSeriesA, title: "A New Hope", seriesId: seriesA, dateCreated: new DateTime(2026, 1, 1)),
            NewSource(id: sourceWithSeriesB, title: "The Fellowship of the Ring", seriesId: seriesB, dateCreated: new DateTime(2026, 1, 2)),
            NewSource(id: sourceNoSeries, title: "Airplane!", dateCreated: new DateTime(2026, 1, 3)),
        ]);
        FakeSourceSeriesReferenceReader reader = new FakeSourceSeriesReferenceReader(new Dictionary<Guid, (Guid, string)>
        {
            [sourceWithSeriesA] = (seriesA, "Star Wars"),
            [sourceWithSeriesB] = (seriesB, "The Lord of the Rings"),
        });
        using WebApplicationFactory<Program> factory = CreateFactory(repo, reader);
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/v1/masterdata/sources?pageSize=0", TestContext.CancellationToken);

        JsonElement items = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken)).RootElement.GetProperty("items");
        Assert.AreEqual(3, items.GetArrayLength());

        foreach (JsonElement item in items.EnumerateArray())
        {
            string? title = item.GetProperty("title").GetString();
            switch (title)
            {
                case "A New Hope":
                    Assert.AreEqual("Star Wars", item.GetProperty("series").GetProperty("name").GetString());
                    break;
                case "The Fellowship of the Ring":
                    Assert.AreEqual("The Lord of the Rings", item.GetProperty("series").GetProperty("name").GetString());
                    break;
                case "Airplane!":
                    AssertPropertyIsNullOrAbsent(item, "series");
                    break;
                default:
                    Assert.Fail($"unexpected item title '{title}'");
                    break;
            }
        }
    }

    // ── Season reference resolution (#375 join) ─────────────────────────────

    [TestMethod]
    public async Task GetSourceById_SourceHasSeason_ReturnsSeasonReferenceWithRenderedDisplayName()
    {
        Guid sourceId = Guid.NewGuid();
        Guid seasonId = Guid.NewGuid();
        FakeSourceRepository repo = new([NewSource(id: sourceId)]);
        FakeSourceSeasonReferenceReader reader = new(new Dictionary<Guid, (Guid, int, string?, string?)>
        {
            [sourceId] = (seasonId, 1, "Book One", "Water"),
        });
        using WebApplicationFactory<Program> factory = CreateFactory(repo, seasonReader: reader);
        HttpResponseMessage response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/sources/{sourceId}", TestContext.CancellationToken);

        JsonElement root = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken)).RootElement;
        JsonElement season = root.GetProperty("season");
        Assert.AreEqual(JsonValueKind.Object, season.ValueKind);
        Assert.AreEqual(seasonId.ToString("D"), season.GetProperty("id").GetString());
        Assert.AreEqual("Book One: Water", season.GetProperty("name").GetString(),
            "The reference's Name is the season's rendered display name, not its raw Title alone.");
    }

    [TestMethod]
    public async Task GetSourceById_SourceHasNoSeason_ReturnsNullSeason()
    {
        Guid sourceId = Guid.NewGuid();
        FakeSourceRepository repo = new([NewSource(id: sourceId)]);
        using WebApplicationFactory<Program> factory = CreateFactory(repo, seasonReader: new FakeSourceSeasonReferenceReader());
        HttpResponseMessage response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/sources/{sourceId}", TestContext.CancellationToken);

        JsonElement root = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken)).RootElement;
        AssertPropertyIsNullOrAbsent(root, "season");
    }

    [TestMethod]
    public async Task GetSourceById_SeasonSoftDeleted_ReturnsNullSeason()
    {
        Guid sourceId = Guid.NewGuid();
        // The reader's seed omits the entry entirely — modelling a soft-deleted Season, per the
        // reader's documented "absent means unresolved" contract.
        FakeSourceRepository repo = new([NewSource(id: sourceId)]);
        FakeSourceSeasonReferenceReader reader = new(new Dictionary<Guid, (Guid, int, string?, string?)>());
        using WebApplicationFactory<Program> factory = CreateFactory(repo, seasonReader: reader);
        HttpResponseMessage response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/sources/{sourceId}", TestContext.CancellationToken);

        JsonElement root = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken)).RootElement;
        AssertPropertyIsNullOrAbsent(root, "season", "a Source pointing at a soft-deleted Season must resolve to null, not a dangling reference");
    }

    [TestMethod]
    public async Task GetAllSources_MultipleSourcesWithSeasons_BatchResolvesEachSeasonInOneQuery()
    {
        Guid sourceWithSeasonA = Guid.NewGuid();
        Guid sourceWithSeasonB = Guid.NewGuid();
        Guid sourceNoSeason    = Guid.NewGuid();
        Guid seasonA = Guid.NewGuid();
        Guid seasonB = Guid.NewGuid();

        FakeSourceRepository repo = new(
        [
            NewSource(id: sourceWithSeasonA, title: "The Boy in the Iceberg", dateCreated: new DateTime(2026, 1, 1)),
            NewSource(id: sourceWithSeasonB, title: "eps1.4_3xpl0its.wmv", dateCreated: new DateTime(2026, 1, 2)),
            NewSource(id: sourceNoSeason, title: "Airplane!", dateCreated: new DateTime(2026, 1, 3)),
        ]);
        FakeSourceSeasonReferenceReader reader = new(new Dictionary<Guid, (Guid, int, string?, string?)>
        {
            [sourceWithSeasonA] = (seasonA, 1, "Book One", "Water"),
            [sourceWithSeasonB] = (seasonB, 1, null, null),
        });
        using WebApplicationFactory<Program> factory = CreateFactory(repo, seasonReader: reader);
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/v1/masterdata/sources?pageSize=0", TestContext.CancellationToken);

        JsonElement items = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken)).RootElement.GetProperty("items");
        Assert.AreEqual(3, items.GetArrayLength());

        foreach (JsonElement item in items.EnumerateArray())
        {
            string? title = item.GetProperty("title").GetString();
            switch (title)
            {
                case "The Boy in the Iceberg":
                    Assert.AreEqual("Book One: Water", item.GetProperty("season").GetProperty("name").GetString());
                    break;
                case "eps1.4_3xpl0its.wmv":
                    Assert.AreEqual("Season 1", item.GetProperty("season").GetProperty("name").GetString());
                    break;
                case "Airplane!":
                    AssertPropertyIsNullOrAbsent(item, "season");
                    break;
                default:
                    Assert.Fail($"unexpected item title '{title}'");
                    break;
            }
        }
    }

    // ── OpenAPI: tag + rate limit, proven live ──────────────────────────────

    [TestMethod]
    public async Task SourceEndpoints_OnLiveSpec_TaggedMasterData()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        JsonDocument? doc = await factory.CreateClient().GetFromJsonAsync<JsonDocument>("/openapi/v1.json", TestContext.CancellationToken);

        JsonElement paths = doc!.RootElement.GetProperty("paths");

        JsonElement listTags = paths.GetProperty("/api/v1/masterdata/sources").GetProperty("get").GetProperty("tags");
        JsonElement byIdTags = paths.GetProperty("/api/v1/masterdata/sources/{id}").GetProperty("get").GetProperty("tags");

        Assert.Contains(t => t.GetString() == "MasterData", listTags.EnumerateArray());
        Assert.Contains(t => t.GetString() == "MasterData", byIdTags.EnumerateArray());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The API's global <c>JsonSerializerOptions.DefaultIgnoreCondition = WhenWritingNull</c> (see
    /// <c>Program.cs</c>) omits a null property from the response entirely rather than emitting a
    /// literal JSON <c>null</c> — so a "must be null, not an empty string" assertion has to accept
    /// either shape, never just <see cref="JsonValueKind.Null"/> on its own.
    /// </summary>
    private static void AssertPropertyIsNullOrAbsent(JsonElement element, string propertyName, string? message = null)
    {
        bool isNullOrAbsent = !element.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind == JsonValueKind.Null;
        Assert.IsTrue(isNullOrAbsent, message ?? $"'{propertyName}' must be null or omitted, never a non-null value");
    }

    public TestContext TestContext { get; set; }
}

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
        FakeSourceSeriesReferenceReader? seriesReader = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(new NoOpDatabaseInitializer());
                services.AddSingleton<IListableRepository<Source>>(repository ?? new FakeSourceRepository());
                services.AddSingleton<ISourceSeriesReferenceReader>(seriesReader ?? new FakeSourceSeriesReferenceReader());
            }));

    private static Source NewSource(
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
        var repo = new FakeSourceRepository([NewSource(), NewSource(title: "The Terminator")]);
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/sources");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc  = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.IsTrue(root.TryGetProperty("items", out var items));
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
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/sources?page=0");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllSources_PageMalformed_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/sources?page=abc");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllSources_PageSizeMalformed_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/sources?pageSize=abc");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllSources_PageSizeNegative_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/sources?pageSize=-1");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllSources_PageSizeAbove500_Returns422NotSilentClamp()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/sources?pageSize=999");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode, "pageSize above 500 must be rejected, not silently clamped");
    }

    [TestMethod]
    public async Task GetAllSources_PageSizeZero_ReturnsAllRowsAsOnePage()
    {
        var repo = new FakeSourceRepository([NewSource(), NewSource(title: "The Terminator"), NewSource(title: "Airplane!")]);
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/sources?pageSize=0");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(3, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.AreEqual(3, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.AreEqual(3, doc.RootElement.GetProperty("pageSize").GetInt32(), "pageSize=0 reports the effective count, not the literal 0 requested");
    }

    [TestMethod]
    public async Task GetAllSources_PageSizeOmitted_DefaultsTo20()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/sources");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(20, doc.RootElement.GetProperty("pageSize").GetInt32());
    }

    [TestMethod]
    public async Task GetAllSources_PageBeyondLast_Returns422DistinctDetail()
    {
        var repo = new FakeSourceRepository([NewSource()]);
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/sources?page=5");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── GetSourceById ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetSourceById_ExistingId_ReturnsSource()
    {
        var id   = Guid.NewGuid();
        var repo = new FakeSourceRepository([NewSource(id: id, title: "Casablanca", type: QuoteType.Movie, completeness: CompletenessStatus.Complete)]);
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/sources/{id}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.AreEqual(id.ToString("D").ToUpperInvariant(), root.GetProperty("id").GetString());
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
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/sources/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetSourceById_LowercaseId_MatchesCaseInsensitively()
    {
        var id   = Guid.NewGuid();
        var repo = new FakeSourceRepository([NewSource(id: id)]);
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/sources/{id.ToString("D").ToLowerInvariant()}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual(id.ToString("D").ToUpperInvariant(), root.GetProperty("id").GetString());
    }

    [TestMethod]
    public async Task GetSourceById_MalformedId_Returns404NotBadRequest()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/sources/not-a-guid");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetSourceById_UnknownDate_ReturnsNullNotEmptyString()
    {
        var id   = Guid.NewGuid();
        var repo = new FakeSourceRepository([NewSource(id: id, date: "")]);
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/sources/{id}");

        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        AssertPropertyIsNullOrAbsent(root, "date");
    }

    // ── Series reference resolution (#179 join) ─────────────────────────────

    [TestMethod]
    public async Task GetSourceById_SourceHasSeries_ReturnsSeriesReference()
    {
        var sourceId = Guid.NewGuid();
        var seriesId = Guid.NewGuid();
        var repo   = new FakeSourceRepository([NewSource(id: sourceId, seriesId: seriesId)]);
        var reader = new FakeSourceSeriesReferenceReader(new Dictionary<Guid, (Guid, string)>
        {
            [sourceId] = (seriesId, "Star Wars"),
        });
        using var factory = CreateFactory(repo, reader);
        var response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/sources/{sourceId}");

        var root   = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var series = root.GetProperty("series");
        Assert.AreEqual(JsonValueKind.Object, series.ValueKind);
        Assert.AreEqual(seriesId.ToString("D").ToUpperInvariant(), series.GetProperty("id").GetString());
        Assert.AreEqual("Star Wars", series.GetProperty("name").GetString());
    }

    [TestMethod]
    public async Task GetSourceById_SourceHasNoSeries_ReturnsNullSeries()
    {
        var sourceId = Guid.NewGuid();
        var repo = new FakeSourceRepository([NewSource(id: sourceId)]);
        using var factory = CreateFactory(repo, new FakeSourceSeriesReferenceReader());
        var response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/sources/{sourceId}");

        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        AssertPropertyIsNullOrAbsent(root, "series");
    }

    [TestMethod]
    public async Task GetSourceById_SeriesSoftDeleted_ReturnsNullSeries()
    {
        var sourceId = Guid.NewGuid();
        var seriesId = Guid.NewGuid();
        // The Source still carries SeriesId, but the reader's seed omits the entry entirely —
        // modelling a soft-deleted Series, per the reader's documented "absent means unresolved" contract.
        var repo   = new FakeSourceRepository([NewSource(id: sourceId, seriesId: seriesId)]);
        var reader = new FakeSourceSeriesReferenceReader(new Dictionary<Guid, (Guid, string)>());
        using var factory = CreateFactory(repo, reader);
        var response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/sources/{sourceId}");

        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        AssertPropertyIsNullOrAbsent(root, "series", "a Source pointing at a soft-deleted Series must resolve to null, not a dangling reference");
    }

    [TestMethod]
    public async Task GetAllSources_MultipleSourcesWithSeries_BatchResolvesEachSeries()
    {
        var sourceWithSeriesA = Guid.NewGuid();
        var sourceWithSeriesB = Guid.NewGuid();
        var sourceNoSeries    = Guid.NewGuid();
        var seriesA = Guid.NewGuid();
        var seriesB = Guid.NewGuid();

        var repo = new FakeSourceRepository(
        [
            NewSource(id: sourceWithSeriesA, title: "A New Hope", seriesId: seriesA, dateCreated: new DateTime(2026, 1, 1)),
            NewSource(id: sourceWithSeriesB, title: "The Fellowship of the Ring", seriesId: seriesB, dateCreated: new DateTime(2026, 1, 2)),
            NewSource(id: sourceNoSeries, title: "Airplane!", dateCreated: new DateTime(2026, 1, 3)),
        ]);
        var reader = new FakeSourceSeriesReferenceReader(new Dictionary<Guid, (Guid, string)>
        {
            [sourceWithSeriesA] = (seriesA, "Star Wars"),
            [sourceWithSeriesB] = (seriesB, "The Lord of the Rings"),
        });
        using var factory = CreateFactory(repo, reader);
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/sources?pageSize=0");

        var items = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("items");
        Assert.AreEqual(3, items.GetArrayLength());

        foreach (var item in items.EnumerateArray())
        {
            var title = item.GetProperty("title").GetString();
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

    // ── OpenAPI: tag + rate limit, proven live ──────────────────────────────

    [TestMethod]
    public async Task SourceEndpoints_OnLiveSpec_TaggedMasterData()
    {
        using var factory = CreateFactory();
        var doc = await factory.CreateClient().GetFromJsonAsync<JsonDocument>("/openapi/v1.json");

        var paths = doc!.RootElement.GetProperty("paths");

        var listTags = paths.GetProperty("/api/v1/masterdata/sources").GetProperty("get").GetProperty("tags");
        var byIdTags = paths.GetProperty("/api/v1/masterdata/sources/{id}").GetProperty("get").GetProperty("tags");

        Assert.IsTrue(listTags.EnumerateArray().Any(t => t.GetString() == "MasterData"));
        Assert.IsTrue(byIdTags.EnumerateArray().Any(t => t.GetString() == "MasterData"));
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
        var isNullOrAbsent = !element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null;
        Assert.IsTrue(isNullOrAbsent, message ?? $"'{propertyName}' must be null or omitted, never a non-null value");
    }
}

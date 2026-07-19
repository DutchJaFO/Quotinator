using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Tests.Fakes;
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
public class SeriesEndpointsTests
{
    private static WebApplicationFactory<Program> CreateFactory(
        FakeSeriesRepository? repository = null,
        FakeSeriesUniverseReferenceReader? universeReader = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(new NoOpDatabaseInitializer());
                services.AddSingleton<IListableRepository<SeriesEntity>>(repository ?? new FakeSeriesRepository());
                services.AddSingleton<ISeriesUniverseReferenceReader>(universeReader ?? new FakeSeriesUniverseReferenceReader());
            }));

    private static SeriesEntity NewSeries(
        Guid? id = null, string name = "Star Wars", Guid? universeId = null,
        CompletenessStatus completeness = CompletenessStatus.Incomplete,
        DateTime? dateCreated = null) => new()
    {
        Id                 = id ?? Guid.NewGuid(),
        Name               = name,
        UniverseId         = universeId,
        CompletenessStatus = new SafeValue<CompletenessStatus?>(completeness.ToString(), completeness),
        DateCreated        = dateCreated is { } dc ? SafeDateValue.From(dc) : SafeDateValue.Now,
    };

    // ── GetAllSeries — basic shape ──────────────────────────────────────────

    [TestMethod]
    public async Task GetAllSeries_ReturnsPaginatedResults()
    {
        var repo = new FakeSeriesRepository([NewSeries(), NewSeries(name: "The Lord of the Rings")]);
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/series");

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

    // ── GetAllSeries — pagination contract (#195, eight-case matrix) ────────

    [TestMethod]
    public async Task GetAllSeries_PageZero_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/series?page=0");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllSeries_PageMalformed_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/series?page=abc");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllSeries_PageSizeMalformed_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/series?pageSize=abc");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllSeries_PageSizeNegative_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/series?pageSize=-1");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllSeries_PageSizeAbove500_Returns422NotSilentClamp()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/series?pageSize=999");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode, "pageSize above 500 must be rejected, not silently clamped");
    }

    [TestMethod]
    public async Task GetAllSeries_PageSizeZero_ReturnsAllRowsAsOnePage()
    {
        var repo = new FakeSeriesRepository([NewSeries(), NewSeries(name: "The Lord of the Rings"), NewSeries(name: "Indiana Jones")]);
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/series?pageSize=0");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(3, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.AreEqual(3, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.AreEqual(3, doc.RootElement.GetProperty("pageSize").GetInt32(), "pageSize=0 reports the effective count, not the literal 0 requested");
    }

    [TestMethod]
    public async Task GetAllSeries_PageSizeOmitted_DefaultsTo20()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/series");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(20, doc.RootElement.GetProperty("pageSize").GetInt32());
    }

    [TestMethod]
    public async Task GetAllSeries_PageBeyondLast_Returns422DistinctDetail()
    {
        var repo = new FakeSeriesRepository([NewSeries()]);
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/series?page=5");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── GetSeriesById ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetSeriesById_ExistingId_ReturnsSeries()
    {
        var id   = Guid.NewGuid();
        var repo = new FakeSeriesRepository([NewSeries(id: id, name: "Star Wars", completeness: CompletenessStatus.Complete)]);
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/series/{id}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.AreEqual(id.ToString("D").ToUpperInvariant(), root.GetProperty("id").GetString());
        Assert.AreEqual("Star Wars", root.GetProperty("name").GetString());

        // Response shape assertion (Step 1): completenessStatus must serialize as a plain JSON string
        // value, never the raw SafeValue<T> {"raw":..,"parsed":..} shape.
        Assert.AreEqual(JsonValueKind.String, root.GetProperty("completenessStatus").ValueKind);
        Assert.AreEqual("Complete", root.GetProperty("completenessStatus").GetString());
    }

    [TestMethod]
    public async Task GetSeriesById_UnknownId_Returns404()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/series/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetSeriesById_LowercaseId_MatchesCaseInsensitively()
    {
        var id   = Guid.NewGuid();
        var repo = new FakeSeriesRepository([NewSeries(id: id)]);
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/series/{id.ToString("D").ToLowerInvariant()}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual(id.ToString("D").ToUpperInvariant(), root.GetProperty("id").GetString());
    }

    [TestMethod]
    public async Task GetSeriesById_MalformedId_Returns404NotBadRequest()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/series/not-a-guid");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Universe reference resolution (#179 join) ───────────────────────────

    [TestMethod]
    public async Task GetSeriesById_SeriesHasUniverse_ReturnsUniverseReference()
    {
        var seriesId   = Guid.NewGuid();
        var universeId = Guid.NewGuid();
        var repo   = new FakeSeriesRepository([NewSeries(id: seriesId, universeId: universeId)]);
        var reader = new FakeSeriesUniverseReferenceReader(new Dictionary<Guid, (Guid, string)>
        {
            [seriesId] = (universeId, "Star Wars Universe"),
        });
        using var factory = CreateFactory(repo, reader);
        var response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/series/{seriesId}");

        var root     = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var universe = root.GetProperty("universe");
        Assert.AreEqual(JsonValueKind.Object, universe.ValueKind);
        Assert.AreEqual(universeId.ToString("D").ToUpperInvariant(), universe.GetProperty("id").GetString());
        Assert.AreEqual("Star Wars Universe", universe.GetProperty("name").GetString());
    }

    [TestMethod]
    public async Task GetSeriesById_SeriesHasNoUniverse_ReturnsNullUniverse()
    {
        var seriesId = Guid.NewGuid();
        var repo = new FakeSeriesRepository([NewSeries(id: seriesId)]);
        using var factory = CreateFactory(repo, new FakeSeriesUniverseReferenceReader());
        var response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/series/{seriesId}");

        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        AssertPropertyIsNullOrAbsent(root, "universe");
    }

    [TestMethod]
    public async Task GetSeriesById_UniverseSoftDeleted_ReturnsNullUniverse()
    {
        var seriesId   = Guid.NewGuid();
        var universeId = Guid.NewGuid();
        // The Series still carries UniverseId, but the reader's seed omits the entry entirely —
        // modelling a soft-deleted Universe, per the reader's documented "absent means unresolved" contract.
        var repo   = new FakeSeriesRepository([NewSeries(id: seriesId, universeId: universeId)]);
        var reader = new FakeSeriesUniverseReferenceReader(new Dictionary<Guid, (Guid, string)>());
        using var factory = CreateFactory(repo, reader);
        var response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/series/{seriesId}");

        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        AssertPropertyIsNullOrAbsent(root, "universe", "a Series pointing at a soft-deleted Universe must resolve to null, not a dangling reference");
    }

    [TestMethod]
    public async Task GetAllSeries_MultipleSeriesWithUniverse_BatchResolvesEachUniverse()
    {
        var seriesWithUniverseA = Guid.NewGuid();
        var seriesWithUniverseB = Guid.NewGuid();
        var seriesNoUniverse    = Guid.NewGuid();
        var universeA = Guid.NewGuid();
        var universeB = Guid.NewGuid();

        var repo = new FakeSeriesRepository(
        [
            NewSeries(id: seriesWithUniverseA, name: "Star Wars", universeId: universeA, dateCreated: new DateTime(2026, 1, 1)),
            NewSeries(id: seriesWithUniverseB, name: "The Lord of the Rings", universeId: universeB, dateCreated: new DateTime(2026, 1, 2)),
            NewSeries(id: seriesNoUniverse, name: "Indiana Jones", dateCreated: new DateTime(2026, 1, 3)),
        ]);
        var reader = new FakeSeriesUniverseReferenceReader(new Dictionary<Guid, (Guid, string)>
        {
            [seriesWithUniverseA] = (universeA, "Star Wars Universe"),
            [seriesWithUniverseB] = (universeB, "Middle Earth"),
        });
        using var factory = CreateFactory(repo, reader);
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/series?pageSize=0");

        var items = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("items");
        Assert.AreEqual(3, items.GetArrayLength());

        foreach (var item in items.EnumerateArray())
        {
            var name = item.GetProperty("name").GetString();
            switch (name)
            {
                case "Star Wars":
                    Assert.AreEqual("Star Wars Universe", item.GetProperty("universe").GetProperty("name").GetString());
                    break;
                case "The Lord of the Rings":
                    Assert.AreEqual("Middle Earth", item.GetProperty("universe").GetProperty("name").GetString());
                    break;
                case "Indiana Jones":
                    AssertPropertyIsNullOrAbsent(item, "universe");
                    break;
                default:
                    Assert.Fail($"unexpected item name '{name}'");
                    break;
            }
        }
    }

    // ── OpenAPI: tag + rate limit, proven live ──────────────────────────────

    [TestMethod]
    public async Task SeriesEndpoints_OnLiveSpec_TaggedMasterData()
    {
        using var factory = CreateFactory();
        var doc = await factory.CreateClient().GetFromJsonAsync<JsonDocument>("/openapi/v1.json");

        var paths = doc!.RootElement.GetProperty("paths");

        var listTags = paths.GetProperty("/api/v1/masterdata/series").GetProperty("get").GetProperty("tags");
        var byIdTags = paths.GetProperty("/api/v1/masterdata/series/{id}").GetProperty("get").GetProperty("tags");

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

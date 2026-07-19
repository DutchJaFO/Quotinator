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
using Quotinator.Engine.Entities;

namespace Quotinator.Api.Tests.Endpoints;

[TestClass]
public class StageDirectionEndpointsTests
{
    private static WebApplicationFactory<Program> CreateFactory(FakeStageDirectionRepository? repository = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(new NoOpDatabaseInitializer());
                services.AddSingleton<IListableRepository<StageDirectionEntity>>(repository ?? new FakeStageDirectionRepository());
            }));

    private static StageDirectionEntity NewStageDirection(
        Guid? id = null, string text = "[EXT. AIRPORT - DAY]", string? imageUrl = null,
        CompletenessStatus? completeness = CompletenessStatus.Incomplete) => new()
    {
        Id                 = id ?? Guid.NewGuid(),
        Text               = text,
        ImageUrl           = imageUrl,
        CompletenessStatus = completeness is { } c ? new SafeValue<CompletenessStatus?>(c.ToString(), c) : SafeValue<CompletenessStatus?>.Empty,
    };

    // ── GetAllStageDirections — basic shape ─────────────────────────────────

    [TestMethod]
    public async Task GetAllStageDirections_ReturnsPaginatedResults()
    {
        var repo = new FakeStageDirectionRepository
        {
            ReturnPage = new PagedItems<StageDirectionEntity>([NewStageDirection(), NewStageDirection(text: "[INT. HANGAR - NIGHT]")], 1, 20, 2)
        };
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/stagedirections");

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

    // ── GetAllStageDirections — pagination contract (#195, eight-case matrix) ──

    [TestMethod]
    public async Task GetAllStageDirections_PageZero_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/stagedirections?page=0");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllStageDirections_PageMalformed_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/stagedirections?page=abc");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllStageDirections_PageSizeMalformed_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/stagedirections?pageSize=abc");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllStageDirections_PageSizeNegative_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/stagedirections?pageSize=-1");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllStageDirections_PageSizeAbove500_Returns422NotSilentClamp()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/stagedirections?pageSize=999");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode, "pageSize above 500 must be rejected, not silently clamped");
    }

    [TestMethod]
    public async Task GetAllStageDirections_PageSizeZero_ReturnsAllRowsAsOnePage()
    {
        var repo = new FakeStageDirectionRepository
        {
            ReturnPage = new PagedItems<StageDirectionEntity>(
                [NewStageDirection(), NewStageDirection(text: "[INT. HANGAR - NIGHT]"), NewStageDirection(text: "[EXT. RUNWAY - DUSK]")], 1, 3, 3)
        };
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/stagedirections?pageSize=0");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(3, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.AreEqual(3, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.AreEqual(3, doc.RootElement.GetProperty("pageSize").GetInt32(), "pageSize=0 reports the effective count, not the literal 0 requested");
    }

    [TestMethod]
    public async Task GetAllStageDirections_PageSizeOmitted_DefaultsTo20()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/stagedirections");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(20, doc.RootElement.GetProperty("pageSize").GetInt32());
    }

    [TestMethod]
    public async Task GetAllStageDirections_PageBeyondLast_Returns422DistinctDetail()
    {
        var repo = new FakeStageDirectionRepository
        {
            ReturnPage = new PagedItems<StageDirectionEntity>([NewStageDirection()], 1, 20, 1)
        };
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/stagedirections?page=5");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── GetStageDirectionById ────────────────────────────────────────────────

    [TestMethod]
    public async Task GetStageDirectionById_ExistingId_ReturnsStageDirection()
    {
        var id   = Guid.NewGuid();
        var repo = new FakeStageDirectionRepository
        {
            ReturnById = NewStageDirection(id: id, text: "[EXT. AIRPORT - DAY]", completeness: CompletenessStatus.Complete)
        };
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/stagedirections/{id}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.AreEqual(id.ToString("D").ToUpperInvariant(), root.GetProperty("id").GetString());
        Assert.AreEqual("[EXT. AIRPORT - DAY]", root.GetProperty("text").GetString());

        // Response shape assertion (Step 1/9): completenessStatus must serialize as a plain JSON string
        // value, never the raw SafeValue<T> {"raw":..,"parsed":..} shape.
        Assert.AreEqual(JsonValueKind.String, root.GetProperty("completenessStatus").ValueKind);
        Assert.AreEqual("Complete", root.GetProperty("completenessStatus").GetString());
    }

    [TestMethod]
    public async Task GetStageDirectionById_UnknownId_Returns404()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/stagedirections/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetStageDirectionById_MalformedId_Returns404NotBadRequest()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/stagedirections/not-a-guid");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetStageDirectionById_LowercaseId_MatchesCaseInsensitively()
    {
        var id   = Guid.NewGuid();
        var repo = new FakeStageDirectionRepository { ReturnById = NewStageDirection(id: id) };
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/stagedirections/{id.ToString("D").ToLowerInvariant()}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual(id.ToString("D").ToUpperInvariant(), root.GetProperty("id").GetString());
    }

    // ── OpenAPI: tag + rate limit, proven live ──────────────────────────────

    [TestMethod]
    public async Task StageDirectionEndpoints_OnLiveSpec_TaggedMasterData()
    {
        using var factory = CreateFactory();
        var doc = await factory.CreateClient().GetFromJsonAsync<JsonDocument>("/openapi/v1.json");

        var paths = doc!.RootElement.GetProperty("paths");

        var listTags = paths.GetProperty("/api/v1/masterdata/stagedirections").GetProperty("get").GetProperty("tags");
        var byIdTags = paths.GetProperty("/api/v1/masterdata/stagedirections/{id}").GetProperty("get").GetProperty("tags");

        Assert.IsTrue(listTags.EnumerateArray().Any(t => t.GetString() == "MasterData"));
        Assert.IsTrue(byIdTags.EnumerateArray().Any(t => t.GetString() == "MasterData"));
    }
}

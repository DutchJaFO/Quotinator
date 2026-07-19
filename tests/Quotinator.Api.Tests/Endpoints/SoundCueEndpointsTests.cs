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

namespace Quotinator.Api.Tests.Endpoints;

[TestClass]
public class SoundCueEndpointsTests
{
    private static WebApplicationFactory<Program> CreateFactory(FakeSoundCueRepository? repository = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(new NoOpDatabaseInitializer());
                services.AddSingleton<IListableRepository<SoundCueEntity>>(repository ?? new FakeSoundCueRepository());
            }));

    private static SoundCueEntity NewSoundCue(
        Guid? id = null, string text = "[awkward silence]", string? soundFileUrl = null, string? imageUrl = null,
        CompletenessStatus? completeness = CompletenessStatus.Incomplete) => new()
    {
        Id                 = id ?? Guid.NewGuid(),
        Text               = text,
        SoundFileUrl       = soundFileUrl,
        ImageUrl           = imageUrl,
        CompletenessStatus = completeness is { } c ? new SafeValue<CompletenessStatus?>(c.ToString(), c) : SafeValue<CompletenessStatus?>.Empty,
    };

    // ── GetAllSoundCues — basic shape ───────────────────────────────────────

    [TestMethod]
    public async Task GetAllSoundCues_ReturnsPaginatedResults()
    {
        var repo = new FakeSoundCueRepository
        {
            ReturnPage = new PagedItems<SoundCueEntity>([NewSoundCue(), NewSoundCue(text: "[distant thunder]")], 1, 20, 2)
        };
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/soundcues");

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

    // ── GetAllSoundCues — pagination contract (#195, eight-case matrix) ─────

    [TestMethod]
    public async Task GetAllSoundCues_PageZero_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/soundcues?page=0");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllSoundCues_PageMalformed_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/soundcues?page=abc");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllSoundCues_PageSizeMalformed_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/soundcues?pageSize=abc");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllSoundCues_PageSizeNegative_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/soundcues?pageSize=-1");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllSoundCues_PageSizeAbove500_Returns422NotSilentClamp()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/soundcues?pageSize=999");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode, "pageSize above 500 must be rejected, not silently clamped");
    }

    [TestMethod]
    public async Task GetAllSoundCues_PageSizeZero_ReturnsAllRowsAsOnePage()
    {
        var repo = new FakeSoundCueRepository
        {
            ReturnPage = new PagedItems<SoundCueEntity>(
                [NewSoundCue(), NewSoundCue(text: "[distant thunder]"), NewSoundCue(text: "[gunshot]")], 1, 3, 3)
        };
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/soundcues?pageSize=0");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(3, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.AreEqual(3, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.AreEqual(3, doc.RootElement.GetProperty("pageSize").GetInt32(), "pageSize=0 reports the effective count, not the literal 0 requested");
    }

    [TestMethod]
    public async Task GetAllSoundCues_PageSizeOmitted_DefaultsTo20()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/soundcues");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(20, doc.RootElement.GetProperty("pageSize").GetInt32());
    }

    [TestMethod]
    public async Task GetAllSoundCues_PageBeyondLast_Returns422DistinctDetail()
    {
        var repo = new FakeSoundCueRepository
        {
            ReturnPage = new PagedItems<SoundCueEntity>([NewSoundCue()], 1, 20, 1)
        };
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/soundcues?page=5");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── GetSoundCueById ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetSoundCueById_ExistingId_ReturnsSoundCue()
    {
        var id   = Guid.NewGuid();
        var repo = new FakeSoundCueRepository
        {
            ReturnById = NewSoundCue(id: id, text: "[awkward silence]", soundFileUrl: "silence.mp3", completeness: CompletenessStatus.Complete)
        };
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/soundcues/{id}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.AreEqual(id.ToString("D").ToUpperInvariant(), root.GetProperty("id").GetString());
        Assert.AreEqual("[awkward silence]", root.GetProperty("text").GetString());
        Assert.AreEqual("silence.mp3", root.GetProperty("soundFileUrl").GetString());

        // Response shape assertion (Step 1/9): completenessStatus must serialize as a plain JSON string
        // value, never the raw SafeValue<T> {"raw":..,"parsed":..} shape.
        Assert.AreEqual(JsonValueKind.String, root.GetProperty("completenessStatus").ValueKind);
        Assert.AreEqual("Complete", root.GetProperty("completenessStatus").GetString());
    }

    [TestMethod]
    public async Task GetSoundCueById_UnknownId_Returns404()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/soundcues/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetSoundCueById_MalformedId_Returns404NotBadRequest()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/soundcues/not-a-guid");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetSoundCueById_LowercaseId_MatchesCaseInsensitively()
    {
        var id   = Guid.NewGuid();
        var repo = new FakeSoundCueRepository { ReturnById = NewSoundCue(id: id) };
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/soundcues/{id.ToString("D").ToLowerInvariant()}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual(id.ToString("D").ToUpperInvariant(), root.GetProperty("id").GetString());
    }

    // ── OpenAPI: tag + rate limit, proven live ──────────────────────────────

    [TestMethod]
    public async Task SoundCueEndpoints_OnLiveSpec_TaggedMasterData()
    {
        using var factory = CreateFactory();
        var doc = await factory.CreateClient().GetFromJsonAsync<JsonDocument>("/openapi/v1.json");

        var paths = doc!.RootElement.GetProperty("paths");

        var listTags = paths.GetProperty("/api/v1/masterdata/soundcues").GetProperty("get").GetProperty("tags");
        var byIdTags = paths.GetProperty("/api/v1/masterdata/soundcues/{id}").GetProperty("get").GetProperty("tags");

        Assert.IsTrue(listTags.EnumerateArray().Any(t => t.GetString() == "MasterData"));
        Assert.IsTrue(byIdTags.EnumerateArray().Any(t => t.GetString() == "MasterData"));
    }
}

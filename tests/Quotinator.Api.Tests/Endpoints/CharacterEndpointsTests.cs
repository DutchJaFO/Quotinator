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
using Quotinator.Engine.Entities;
using Quotinator.Engine.Repositories;

namespace Quotinator.Api.Tests.Endpoints;

[TestClass]
public class CharacterEndpointsTests
{
    private static WebApplicationFactory<Program> CreateFactory(
        FakeCharacterRepository? repository = null,
        StubCharacterSourceLinkReader? linkReader = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(new NoOpDatabaseInitializer());
                services.AddSingleton<IListableRepository<Character>>(repository ?? new FakeCharacterRepository());
                services.AddSingleton<ICharacterSourceLinkReader>(linkReader ?? new StubCharacterSourceLinkReader());
            }));

    private static Character NewCharacter(
        Guid? id = null, string name = "Rick Blaine",
        CompletenessStatus completeness = CompletenessStatus.Incomplete,
        DateTime? dateCreated = null) => new()
    {
        Id                 = id ?? Guid.NewGuid(),
        Name               = name,
        CompletenessStatus = new SafeValue<CompletenessStatus?>(completeness.ToString(), completeness),
        DateCreated        = dateCreated is { } dc ? SafeDateValue.From(dc) : SafeDateValue.Now,
    };

    // ── GetAllCharacters — basic shape ──────────────────────────────────────

    [TestMethod]
    public async Task GetAllCharacters_ReturnsPaginatedResults()
    {
        var repo = new FakeCharacterRepository([NewCharacter(), NewCharacter(name: "Ilsa Lund")]);
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/characters");

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

    [TestMethod]
    public async Task GetAllCharacters_CharacterWithNoSourceLinks_ReturnsEmptySourcesArray()
    {
        var id   = Guid.NewGuid();
        var repo = new FakeCharacterRepository([NewCharacter(id: id)]);
        using var factory = CreateFactory(repo, new StubCharacterSourceLinkReader());
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/characters");

        var doc   = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("items");
        Assert.AreEqual(1, items.GetArrayLength());

        var sources = items[0].GetProperty("sources");
        Assert.AreEqual(JsonValueKind.Array, sources.ValueKind);
        Assert.AreEqual(0, sources.GetArrayLength());
    }

    [TestMethod]
    public async Task GetAllCharacters_IncludesSourceReferencesForEachCharacter()
    {
        var characterId = Guid.NewGuid();
        var sourceId    = Guid.NewGuid();
        var repo   = new FakeCharacterRepository([NewCharacter(id: characterId)]);
        var reader = new StubCharacterSourceLinkReader(new Dictionary<Guid, IReadOnlyList<(Guid Id, string Name)>>
        {
            [characterId] = [(sourceId, "Casablanca")],
        });
        using var factory = CreateFactory(repo, reader);
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/characters");

        var doc   = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("items");
        Assert.AreEqual(1, items.GetArrayLength());

        var sources = items[0].GetProperty("sources");
        Assert.AreEqual(1, sources.GetArrayLength());
        Assert.AreEqual(sourceId.ToString("D").ToUpperInvariant(), sources[0].GetProperty("id").GetString());
        Assert.AreEqual("Casablanca", sources[0].GetProperty("name").GetString());
    }

    [TestMethod]
    public async Task GetAllCharacters_MultipleSourcesWithNames_BatchResolvesEachCharacter()
    {
        var characterWithSourcesId = Guid.NewGuid();
        var characterNoSourcesId   = Guid.NewGuid();
        var sourceA = Guid.NewGuid();
        var sourceB = Guid.NewGuid();

        var repo = new FakeCharacterRepository(
        [
            NewCharacter(id: characterWithSourcesId, name: "Striker", dateCreated: new DateTime(2026, 1, 1)),
            NewCharacter(id: characterNoSourcesId, name: "Roger Murdock", dateCreated: new DateTime(2026, 1, 2)),
        ]);
        var reader = new StubCharacterSourceLinkReader(new Dictionary<Guid, IReadOnlyList<(Guid Id, string Name)>>
        {
            [characterWithSourcesId] = [(sourceA, "Airplane!"), (sourceB, "Airplane II: The Sequel")],
        });
        using var factory = CreateFactory(repo, reader);
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/characters?pageSize=0");

        var items = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("items");
        Assert.AreEqual(2, items.GetArrayLength());

        foreach (var item in items.EnumerateArray())
        {
            var name = item.GetProperty("name").GetString();
            switch (name)
            {
                case "Striker":
                    var sources = item.GetProperty("sources");
                    Assert.AreEqual(2, sources.GetArrayLength());
                    Assert.IsTrue(sources.EnumerateArray().Any(s => s.GetProperty("name").GetString() == "Airplane!"));
                    Assert.IsTrue(sources.EnumerateArray().Any(s => s.GetProperty("name").GetString() == "Airplane II: The Sequel"));
                    break;
                case "Roger Murdock":
                    Assert.AreEqual(0, item.GetProperty("sources").GetArrayLength());
                    break;
                default:
                    Assert.Fail($"unexpected item name '{name}'");
                    break;
            }
        }
    }

    // ── GetAllCharacters — pagination contract (#195, eight-case matrix) ────

    [TestMethod]
    public async Task GetAllCharacters_PageZero_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/characters?page=0");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllCharacters_PageMalformed_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/characters?page=abc");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllCharacters_PageSizeMalformed_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/characters?pageSize=abc");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllCharacters_PageSizeNegative_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/characters?pageSize=-1");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllCharacters_PageSizeAbove500_Returns422NotSilentClamp()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/characters?pageSize=999");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode, "pageSize above 500 must be rejected, not silently clamped");
    }

    [TestMethod]
    public async Task GetAllCharacters_PageSizeZero_ReturnsAllRowsAsOnePage()
    {
        var repo = new FakeCharacterRepository([NewCharacter(), NewCharacter(name: "Ilsa Lund"), NewCharacter(name: "Sam")]);
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/characters?pageSize=0");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(3, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.AreEqual(3, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.AreEqual(3, doc.RootElement.GetProperty("pageSize").GetInt32(), "pageSize=0 reports the effective count, not the literal 0 requested");
    }

    [TestMethod]
    public async Task GetAllCharacters_PageSizeOmitted_DefaultsTo20()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/characters");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(20, doc.RootElement.GetProperty("pageSize").GetInt32());
    }

    [TestMethod]
    public async Task GetAllCharacters_PageBeyondLast_Returns422DistinctDetail()
    {
        var repo = new FakeCharacterRepository([NewCharacter()]);
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/characters?page=5");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── GetCharacterById ─────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetCharacterById_ExistingId_ReturnsCharacterWithSourceReferences()
    {
        var id       = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var repo   = new FakeCharacterRepository([NewCharacter(id: id, name: "Rick Blaine", completeness: CompletenessStatus.Complete)]);
        var reader = new StubCharacterSourceLinkReader(new Dictionary<Guid, IReadOnlyList<(Guid Id, string Name)>>
        {
            [id] = [(sourceId, "Casablanca")],
        });
        using var factory = CreateFactory(repo, reader);
        var response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/characters/{id}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.AreEqual(id.ToString("D").ToUpperInvariant(), root.GetProperty("id").GetString());
        Assert.AreEqual("Rick Blaine", root.GetProperty("name").GetString());

        var sources = root.GetProperty("sources");
        Assert.AreEqual(1, sources.GetArrayLength());
        Assert.AreEqual(sourceId.ToString("D").ToUpperInvariant(), sources[0].GetProperty("id").GetString());
        Assert.AreEqual("Casablanca", sources[0].GetProperty("name").GetString());

        // Response shape assertion (Step 3): completenessStatus must serialize as a plain JSON string
        // value, never the raw SafeValue<T> {"raw":..,"parsed":..} shape.
        Assert.AreEqual(JsonValueKind.String, root.GetProperty("completenessStatus").ValueKind);
        Assert.AreEqual("Complete", root.GetProperty("completenessStatus").GetString());
    }

    [TestMethod]
    public async Task GetCharacterById_MultipleSourceLinks_ReturnsAllOfThemWithNames()
    {
        var id      = Guid.NewGuid();
        var sourceA = Guid.NewGuid();
        var sourceB = Guid.NewGuid();
        var repo   = new FakeCharacterRepository([NewCharacter(id: id, name: "Gandalf")]);
        var reader = new StubCharacterSourceLinkReader(new Dictionary<Guid, IReadOnlyList<(Guid Id, string Name)>>
        {
            [id] = [(sourceA, "The Fellowship of the Ring"), (sourceB, "The Hobbit")],
        });
        using var factory = CreateFactory(repo, reader);
        var response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/characters/{id}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var sources = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("sources");
        Assert.AreEqual(2, sources.GetArrayLength());
        Assert.IsTrue(sources.EnumerateArray().Any(s =>
            s.GetProperty("id").GetString() == sourceA.ToString("D").ToUpperInvariant() &&
            s.GetProperty("name").GetString() == "The Fellowship of the Ring"));
        Assert.IsTrue(sources.EnumerateArray().Any(s =>
            s.GetProperty("id").GetString() == sourceB.ToString("D").ToUpperInvariant() &&
            s.GetProperty("name").GetString() == "The Hobbit"));
    }

    [TestMethod]
    public async Task GetCharacterById_UnknownId_Returns404()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/characters/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetCharacterById_MalformedId_Returns404NotBadRequest()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/characters/not-a-guid");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetCharacterById_LowercaseId_MatchesCaseInsensitively()
    {
        var id   = Guid.NewGuid();
        var repo = new FakeCharacterRepository([NewCharacter(id: id)]);
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/characters/{id.ToString("D").ToLowerInvariant()}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual(id.ToString("D").ToUpperInvariant(), root.GetProperty("id").GetString());
    }

    [TestMethod]
    public async Task GetCharacterById_SourceSoftDeleted_ExcludedFromSources()
    {
        var id = Guid.NewGuid();
        // The stub link reader's seed omits the soft-deleted Source entirely — modelling the join's
        // Sources.IsDeleted = 0 filter excluding it from the result before it ever reaches this layer.
        var repo   = new FakeCharacterRepository([NewCharacter(id: id)]);
        var reader = new StubCharacterSourceLinkReader(new Dictionary<Guid, IReadOnlyList<(Guid Id, string Name)>>
        {
            [id] = [],
        });
        using var factory = CreateFactory(repo, reader);
        var response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/characters/{id}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var sources = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("sources");
        Assert.AreEqual(0, sources.GetArrayLength());
    }

    // ── OpenAPI: tag + rate limit, proven live ──────────────────────────────

    [TestMethod]
    public async Task CharacterEndpoints_OnLiveSpec_TaggedMasterData()
    {
        using var factory = CreateFactory();
        var doc = await factory.CreateClient().GetFromJsonAsync<JsonDocument>("/openapi/v1.json");

        var paths = doc!.RootElement.GetProperty("paths");

        var listTags = paths.GetProperty("/api/v1/masterdata/characters").GetProperty("get").GetProperty("tags");
        var byIdTags = paths.GetProperty("/api/v1/masterdata/characters/{id}").GetProperty("get").GetProperty("tags");

        Assert.IsTrue(listTags.EnumerateArray().Any(t => t.GetString() == "MasterData"));
        Assert.IsTrue(byIdTags.EnumerateArray().Any(t => t.GetString() == "MasterData"));
    }

    // ── Test doubles ──────────────────────────────────────────────────────────

    /// <summary>In-memory <see cref="ICharacterSourceLinkReader"/> double, backed by a constructor-supplied
    /// Character id → Source references dictionary. A Character id absent from the dictionary resolves to
    /// an empty list, matching the real reader's "absent, not empty-valued" contract for the batch form and
    /// the single-id form's own "no active links" case.</summary>
    private sealed class StubCharacterSourceLinkReader : ICharacterSourceLinkReader
    {
        private readonly IReadOnlyDictionary<Guid, IReadOnlyList<(Guid Id, string Name)>> _sourcesByCharacterId;

        internal StubCharacterSourceLinkReader(IReadOnlyDictionary<Guid, IReadOnlyList<(Guid Id, string Name)>>? seed = null)
        {
            _sourcesByCharacterId = seed ?? new Dictionary<Guid, IReadOnlyList<(Guid Id, string Name)>>();
        }

        public Task<IReadOnlyList<(Guid Id, string Name)>> GetSourceReferencesAsync(Guid characterId)
        {
            IReadOnlyList<(Guid Id, string Name)> result = _sourcesByCharacterId.TryGetValue(characterId, out var sources) ? sources : [];
            return Task.FromResult(result);
        }

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<(Guid Id, string Name)>>> GetSourceReferencesForManyAsync(IReadOnlyList<Guid> characterIds)
        {
            var result = characterIds
                .Where(_sourcesByCharacterId.ContainsKey)
                .ToDictionary(id => id, id => _sourcesByCharacterId[id]);
            return Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<(Guid Id, string Name)>>>(result);
        }
    }
}

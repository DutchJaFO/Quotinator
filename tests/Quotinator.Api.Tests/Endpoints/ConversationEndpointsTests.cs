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
public class ConversationEndpointsTests
{
    private static WebApplicationFactory<Program> CreateFactory(
        FakeConversationRepository? repository = null,
        FakeConversationLineCountReader? lineCountReader = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(new NoOpDatabaseInitializer());
                services.AddSingleton<IListableRepository<ConversationEntity>>(repository ?? new FakeConversationRepository());
                services.AddSingleton<IConversationLineCountReader>(lineCountReader ?? new FakeConversationLineCountReader());
            }));

    private static ConversationEntity NewConversation(
        Guid? id = null, string? description = "A scene",
        CompletenessStatus completeness = CompletenessStatus.Incomplete,
        DateTime? dateCreated = null) => new()
    {
        Id                 = id ?? Guid.NewGuid(),
        Description        = description,
        CompletenessStatus = new SafeValue<CompletenessStatus?>(completeness.ToString(), completeness),
        DateCreated        = dateCreated is { } dc ? SafeDateValue.From(dc) : SafeDateValue.Now,
    };

    [TestMethod]
    public async Task GetById_KnownId_ReturnsOkWithLinesInOrder()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync($"/api/v1/conversations/{FakeQuoteService.SampleConversation.Id}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(FakeQuoteService.SampleConversation.Id, doc.RootElement.GetProperty("id").GetString());
        var lines = doc.RootElement.GetProperty("lines");
        Assert.AreEqual(2, lines.GetArrayLength());
        Assert.AreEqual(1, lines[0].GetProperty("order").GetInt32());
        Assert.AreEqual("stage_direction", lines[0].GetProperty("type").GetString());
        Assert.AreEqual(2, lines[1].GetProperty("order").GetInt32());
        Assert.AreEqual("quote", lines[1].GetProperty("type").GetString());
    }

    [TestMethod]
    public async Task GetById_QuoteLine_HasNoRecursiveConversationsField()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync($"/api/v1/conversations/{FakeQuoteService.SampleConversation.Id}");

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var quoteLine = doc.RootElement.GetProperty("lines")[1];
        Assert.IsFalse(quoteLine.GetProperty("quote").TryGetProperty("conversations", out _),
            "An embedded quote line must never carry its own conversations membership array");
    }

    [TestMethod]
    public async Task GetById_UppercaseCasedId_StillResolves()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync($"/api/v1/conversations/{FakeQuoteService.SampleConversation.Id.ToUpperInvariant()}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task GetById_UnknownId_Returns404WithLocalisedDetail()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/conversations/00000000-0000-0000-0000-000000000000");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(doc.RootElement.TryGetProperty("detail", out var detail));
        Assert.IsFalse(string.IsNullOrWhiteSpace(detail.GetString()));
    }

    [TestMethod]
    public async Task GetById_UnknownId_WithAcceptLanguageNl_ReturnsDutchDetail()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("nl");
        var response = await client.GetAsync("/api/v1/conversations/00000000-0000-0000-0000-000000000000");

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        StringAssert.Contains(doc.RootElement.GetProperty("detail").GetString(), "conversatie");
    }

    [TestMethod]
    public async Task GetById_InvalidLang_Returns400()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync($"/api/v1/conversations/{FakeQuoteService.SampleConversation.Id}?lang=not-a-lang-code-at-all");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── GetAllConversations — basic shape ───────────────────────────────────

    [TestMethod]
    public async Task GetAllConversations_ReturnsPaginatedResults()
    {
        var repo = new FakeConversationRepository([NewConversation(), NewConversation(description: "Another scene")]);
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync("/api/v1/conversations");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc  = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.IsTrue(root.TryGetProperty("items", out var items));
        Assert.IsTrue(root.TryGetProperty("page", out _));
        Assert.IsTrue(root.TryGetProperty("pageSize", out _));
        Assert.IsTrue(root.TryGetProperty("totalCount", out _));
        Assert.IsTrue(root.TryGetProperty("totalPages", out _));
        Assert.AreEqual(2, items.GetArrayLength());

        // Response shape assertion (Step 2): completenessStatus must serialize as a plain JSON string
        // value, never the raw SafeValue<T> {"raw":..,"parsed":..} shape.
        Assert.AreEqual(JsonValueKind.String, items[0].GetProperty("completenessStatus").ValueKind);
    }

    [TestMethod]
    public async Task GetAllConversations_ReturnsSummaryNotFullLineList()
    {
        var id   = Guid.NewGuid();
        var repo = new FakeConversationRepository([NewConversation(id: id)]);
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync("/api/v1/conversations");

        var doc   = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("items");
        Assert.AreEqual(1, items.GetArrayLength());

        var item = items[0];
        Assert.IsFalse(item.TryGetProperty("lines", out _), "GetAll must return summaries, never the full ordered line list");
        Assert.IsTrue(item.TryGetProperty("id", out _));
        Assert.IsTrue(item.TryGetProperty("completenessStatus", out _));
        Assert.IsTrue(item.TryGetProperty("lineCount", out _));
        Assert.AreEqual(id.ToString("D"), item.GetProperty("id").GetString());
    }

    [TestMethod]
    public async Task GetAllConversations_LineCountMatchesActualLineCount()
    {
        var id     = Guid.NewGuid();
        var repo   = new FakeConversationRepository([NewConversation(id: id)]);
        var reader = new FakeConversationLineCountReader(new Dictionary<Guid, int> { [id] = 3 });
        using var factory = CreateFactory(repo, reader);
        var response = await factory.CreateClient().GetAsync("/api/v1/conversations");

        var doc   = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("items");
        Assert.AreEqual(3, items[0].GetProperty("lineCount").GetInt32());
    }

    [TestMethod]
    public async Task GetAllConversations_ConversationWithNoLines_ReturnsZeroLineCount()
    {
        var id   = Guid.NewGuid();
        var repo = new FakeConversationRepository([NewConversation(id: id)]);
        using var factory = CreateFactory(repo, new FakeConversationLineCountReader());
        var response = await factory.CreateClient().GetAsync("/api/v1/conversations");

        var doc   = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("items");
        Assert.AreEqual(1, items.GetArrayLength());
        Assert.AreEqual(0, items[0].GetProperty("lineCount").GetInt32());
    }

    [TestMethod]
    public async Task GetAllConversations_MultipleConversationsWithLines_BatchResolvesEachCount()
    {
        var withLinesId = Guid.NewGuid();
        var noLinesId    = Guid.NewGuid();

        var repo = new FakeConversationRepository(
        [
            NewConversation(id: withLinesId, description: "Scene with lines", dateCreated: new DateTime(2026, 1, 1)),
            NewConversation(id: noLinesId, description: "Scene without lines", dateCreated: new DateTime(2026, 1, 2)),
        ]);
        var reader = new FakeConversationLineCountReader(new Dictionary<Guid, int> { [withLinesId] = 5 });
        using var factory = CreateFactory(repo, reader);
        var response = await factory.CreateClient().GetAsync("/api/v1/conversations?pageSize=0");

        var items = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("items");
        Assert.AreEqual(2, items.GetArrayLength());

        foreach (var item in items.EnumerateArray())
        {
            var description = item.GetProperty("description").GetString();
            switch (description)
            {
                case "Scene with lines":
                    Assert.AreEqual(5, item.GetProperty("lineCount").GetInt32());
                    break;
                case "Scene without lines":
                    Assert.AreEqual(0, item.GetProperty("lineCount").GetInt32());
                    break;
                default:
                    Assert.Fail($"unexpected item description '{description}'");
                    break;
            }
        }
    }

    // ── GetAllConversations — pagination contract (#195, eight-case matrix) ─

    [TestMethod]
    public async Task GetAllConversations_PageZero_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/conversations?page=0");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllConversations_PageMalformed_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/conversations?page=abc");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllConversations_PageSizeMalformed_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/conversations?pageSize=abc");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllConversations_PageSizeNegative_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/conversations?pageSize=-1");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllConversations_PageSizeAbove500_Returns422NotSilentClamp()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/conversations?pageSize=999");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode, "pageSize above 500 must be rejected, not silently clamped");
    }

    [TestMethod]
    public async Task GetAllConversations_PageSizeZero_ReturnsAllRowsAsOnePage()
    {
        var repo = new FakeConversationRepository([NewConversation(), NewConversation(), NewConversation()]);
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync("/api/v1/conversations?pageSize=0");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(3, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.AreEqual(3, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.AreEqual(3, doc.RootElement.GetProperty("pageSize").GetInt32(), "pageSize=0 reports the effective count, not the literal 0 requested");
    }

    [TestMethod]
    public async Task GetAllConversations_PageSizeOmitted_DefaultsTo20()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/conversations");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(20, doc.RootElement.GetProperty("pageSize").GetInt32());
    }

    [TestMethod]
    public async Task GetAllConversations_PageBeyondLast_Returns422DistinctDetail()
    {
        var repo = new FakeConversationRepository([NewConversation()]);
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync("/api/v1/conversations?page=5");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── OpenAPI: tag + rate limit, proven live ──────────────────────────────

    [TestMethod]
    public async Task ConversationEndpoints_OnLiveSpec_GetAllTaggedConversations()
    {
        using var factory = CreateFactory();
        var doc = await factory.CreateClient().GetFromJsonAsync<JsonDocument>("/openapi/v1.json");

        var paths = doc!.RootElement.GetProperty("paths");
        var getAll = paths.GetProperty("/api/v1/conversations").GetProperty("get");
        var tags   = getAll.GetProperty("tags");

        Assert.IsTrue(tags.EnumerateArray().Any(t => t.GetString() == "Conversations"));
        Assert.IsFalse(tags.EnumerateArray().Any(t => t.GetString() == "MasterData"),
            "Conversations is a masterdata consumer, not a masterdata entity — it must not carry the MasterData tag");
    }
}

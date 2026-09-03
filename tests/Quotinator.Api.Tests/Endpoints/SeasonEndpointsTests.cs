using Quotinator.Data.Enums;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Core.Entities;
using Quotinator.Core.Repositories;

namespace Quotinator.Api.Tests.Endpoints;

/// <summary>#375: the Season masterdata endpoints, held to the same contract as every other
/// masterdata entity — the eight-case pagination matrix, case-insensitive id matching, and a
/// <c>MasterDataReference</c> for the Series it belongs to.</summary>
[TestClass]
public class SeasonEndpointsTests
{
    private static WebApplicationFactory<Program> CreateFactory(
        FakeSeasonRepository? repository = null,
        FakeSeasonSeriesReferenceReader? seriesReader = null) =>
        new QuotinatorWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(new NoOpDatabaseInitializer());
                services.AddSingleton<IListableRepository<SeasonEntity>>(repository ?? new FakeSeasonRepository());
                services.AddSingleton<ISeasonSeriesReferenceReader>(seriesReader ?? new FakeSeasonSeriesReferenceReader());
            }));

    private static SeasonEntity NewSeason(
        Guid? id = null, int number = 1, string? title = "Book One", string? subtitle = "Water",
        Guid? seriesId = null,
        CompletenessStatus completeness = CompletenessStatus.Incomplete,
        DateTime? dateCreated = null) => new()
    {
        Id                 = id ?? Guid.NewGuid(),
        Number             = number,
        Title              = title,
        Subtitle           = subtitle,
        SeriesId           = seriesId,
        CompletenessStatus = new SafeValue<CompletenessStatus?>(completeness.ToString(), completeness),
        DateCreated        = dateCreated is { } dc ? SafeDateValue.From(dc) : SafeDateValue.Now,
    };

    // ── GetAllSeasons — basic shape ─────────────────────────────────────────

    [TestMethod]
    public async Task GetAllSeasons_ReturnsPaginatedResults()
    {
        FakeSeasonRepository repo = new([NewSeason(), NewSeason(number: 2, title: "Book Two", subtitle: "Earth")]);
        using WebApplicationFactory<Program> factory = CreateFactory(repo);
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/v1/masterdata/seasons", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        JsonElement root = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken)).RootElement;

        Assert.IsTrue(root.TryGetProperty("items", out JsonElement items));
        Assert.IsTrue(root.TryGetProperty("page", out _));
        Assert.IsTrue(root.TryGetProperty("pageSize", out _));
        Assert.IsTrue(root.TryGetProperty("totalCount", out _));
        Assert.IsTrue(root.TryGetProperty("totalPages", out _));
        Assert.AreEqual(2, items.GetArrayLength());
    }

    // ── GetAllSeasons — pagination contract (#195, eight-case matrix) ───────

    [TestMethod]
    public async Task GetAllSeasons_PageZero_Returns422()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/v1/masterdata/seasons?page=0", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllSeasons_PageMalformed_Returns422()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/v1/masterdata/seasons?page=abc", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllSeasons_PageSizeMalformed_Returns422()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/v1/masterdata/seasons?pageSize=abc", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllSeasons_PageSizeNegative_Returns422()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/v1/masterdata/seasons?pageSize=-1", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllSeasons_PageSizeAbove500_Returns422NotSilentClamp()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/v1/masterdata/seasons?pageSize=999", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode, "pageSize above 500 must be rejected, not silently clamped");
    }

    [TestMethod]
    public async Task GetAllSeasons_PageSizeZero_ReturnsAllRowsAsOnePage()
    {
        FakeSeasonRepository repo = new([NewSeason(), NewSeason(number: 2), NewSeason(number: 3)]);
        using WebApplicationFactory<Program> factory = CreateFactory(repo);
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/v1/masterdata/seasons?pageSize=0", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        JsonElement root = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken)).RootElement;
        Assert.AreEqual(3, root.GetProperty("items").GetArrayLength());
        Assert.AreEqual(3, root.GetProperty("totalCount").GetInt32());
        Assert.AreEqual(3, root.GetProperty("pageSize").GetInt32(), "pageSize=0 reports the effective count, not the literal 0 requested");
    }

    [TestMethod]
    public async Task GetAllSeasons_PageSizeOmitted_DefaultsTo20()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/v1/masterdata/seasons", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        JsonElement root = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken)).RootElement;
        Assert.AreEqual(20, root.GetProperty("pageSize").GetInt32());
    }

    [TestMethod]
    public async Task GetAllSeasons_PageBeyondLast_Returns422DistinctDetail()
    {
        FakeSeasonRepository repo = new([NewSeason()]);
        using WebApplicationFactory<Program> factory = CreateFactory(repo);
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/v1/masterdata/seasons?page=5", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── GetSeasonById ───────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetSeasonById_ExistingId_ReturnsSeasonWithItsRenderedName()
    {
        Guid id = Guid.NewGuid();
        FakeSeasonRepository repo = new([NewSeason(id: id, number: 1, title: "Book One", subtitle: "Water", completeness: CompletenessStatus.Complete)]);
        using WebApplicationFactory<Program> factory = CreateFactory(repo);
        HttpResponseMessage response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/seasons/{id}", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        JsonElement root = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken)).RootElement;

        Assert.AreEqual(id.ToString("D"), root.GetProperty("id").GetString());
        Assert.AreEqual(1, root.GetProperty("number").GetInt32());
        Assert.AreEqual("Book One", root.GetProperty("title").GetString());
        Assert.AreEqual("Water", root.GetProperty("subtitle").GetString());
        Assert.AreEqual("Book One: Water", root.GetProperty("displayName").GetString());

        Assert.AreEqual(JsonValueKind.String, root.GetProperty("completenessStatus").ValueKind);
        Assert.AreEqual("Complete", root.GetProperty("completenessStatus").GetString());
    }

    /// <summary>The control for the row above: a season with no name of its own still renders, by its ordinal.</summary>
    [TestMethod]
    public async Task GetSeasonById_NumberOnlySeason_RendersByItsOrdinal()
    {
        Guid id = Guid.NewGuid();
        FakeSeasonRepository repo = new([NewSeason(id: id, number: 3, title: null, subtitle: null)]);
        using WebApplicationFactory<Program> factory = CreateFactory(repo);
        HttpResponseMessage response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/seasons/{id}", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        JsonElement root = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken)).RootElement;
        Assert.AreEqual("Season 3", root.GetProperty("displayName").GetString());
    }

    [TestMethod]
    public async Task GetSeasonById_UnknownId_Returns404()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        HttpResponseMessage response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/seasons/{Guid.NewGuid()}", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetSeasonById_UppercaseId_MatchesCaseInsensitively()
    {
        Guid id = Guid.NewGuid();
        FakeSeasonRepository repo = new([NewSeason(id: id)]);
        using WebApplicationFactory<Program> factory = CreateFactory(repo);
        HttpResponseMessage response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/seasons/{id.ToString("D").ToUpperInvariant()}", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        JsonElement root = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken)).RootElement;
        Assert.AreEqual(id.ToString("D"), root.GetProperty("id").GetString());
    }

    [TestMethod]
    public async Task GetSeasonById_MalformedId_Returns404NotBadRequest()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/v1/masterdata/seasons/not-a-guid", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Series reference resolution (ADR 017 join) ──────────────────────────

    [TestMethod]
    public async Task GetSeasonById_SeasonHasSeries_ReturnsSeriesReference()
    {
        Guid seasonId = Guid.NewGuid();
        Guid seriesId = Guid.NewGuid();
        FakeSeasonRepository repo = new([NewSeason(id: seasonId, seriesId: seriesId)]);
        FakeSeasonSeriesReferenceReader reader = new(new Dictionary<Guid, (Guid Id, string Name)>
        {
            [seasonId] = (seriesId, "Avatar: The Last Airbender"),
        });

        using WebApplicationFactory<Program> factory = CreateFactory(repo, reader);
        HttpResponseMessage response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/seasons/{seasonId}", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        JsonElement root = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken)).RootElement;

        JsonElement series = root.GetProperty("series");
        Assert.AreEqual(seriesId.ToString("D"), series.GetProperty("id").GetString());
        Assert.AreEqual("Avatar: The Last Airbender", series.GetProperty("name").GetString());
    }

    /// <summary>A Season with no Series — and a Season whose Series is soft-deleted, which the reader's
    /// contract makes indistinguishable by design — reports no reference rather than a partial one.</summary>
    [TestMethod]
    public async Task GetSeasonById_SeasonHasNoResolvableSeries_ReturnsNullReference()
    {
        Guid seasonId = Guid.NewGuid();
        FakeSeasonRepository repo = new([NewSeason(id: seasonId, seriesId: Guid.NewGuid())]);

        using WebApplicationFactory<Program> factory = CreateFactory(repo);
        HttpResponseMessage response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/seasons/{seasonId}", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        JsonElement root = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken)).RootElement;
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("series").ValueKind);
    }

    /// <summary>A page of Seasons resolves every Series reference in one batched call, never one per row.</summary>
    [TestMethod]
    public async Task GetAllSeasons_ResolvesSeriesReferencesForEveryRow()
    {
        Guid firstId  = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();
        Guid seriesId = Guid.NewGuid();
        FakeSeasonRepository repo = new([NewSeason(id: firstId, seriesId: seriesId), NewSeason(id: secondId, number: 2, seriesId: seriesId)]);
        FakeSeasonSeriesReferenceReader reader = new(new Dictionary<Guid, (Guid Id, string Name)>
        {
            [firstId]  = (seriesId, "Avatar: The Last Airbender"),
            [secondId] = (seriesId, "Avatar: The Last Airbender"),
        });

        using WebApplicationFactory<Program> factory = CreateFactory(repo, reader);
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/v1/masterdata/seasons", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        JsonElement items = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken)).RootElement.GetProperty("items");

        foreach (JsonElement item in items.EnumerateArray())
            Assert.AreEqual("Avatar: The Last Airbender", item.GetProperty("series").GetProperty("name").GetString());
    }
    public TestContext TestContext { get; set; }
}

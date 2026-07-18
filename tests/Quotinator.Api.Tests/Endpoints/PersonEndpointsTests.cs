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
public class PersonEndpointsTests
{
    private static WebApplicationFactory<Program> CreateFactory(FakePersonRepository? repository = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(new NoOpDatabaseInitializer());
                services.AddSingleton<IListableRepository<Person>>(repository ?? new FakePersonRepository());
            }));

    private static Person NewPerson(
        Guid? id = null, string name = "Humphrey Bogart",
        string? dateOfBirth = "1899-12-25", string? dateOfDeath = "1957-01-14",
        CompletenessStatus? completeness = CompletenessStatus.Incomplete) => new()
    {
        Id                 = id ?? Guid.NewGuid(),
        Name               = name,
        DateOfBirth        = string.IsNullOrEmpty(dateOfBirth) ? SafeDateValue.Empty : new SafeValue<DateTime?>(dateOfBirth, DateTime.Parse(dateOfBirth)),
        DateOfDeath        = string.IsNullOrEmpty(dateOfDeath) ? SafeDateValue.Empty : new SafeValue<DateTime?>(dateOfDeath, DateTime.Parse(dateOfDeath)),
        CompletenessStatus = completeness is { } c ? new SafeValue<CompletenessStatus?>(c.ToString(), c) : SafeValue<CompletenessStatus?>.Empty,
    };

    // ── GetAllPeople — basic shape ──────────────────────────────────────────

    [TestMethod]
    public async Task GetAllPeople_ReturnsPaginatedResults()
    {
        var repo = new FakePersonRepository
        {
            ReturnPage = new PagedItems<Person>([NewPerson(), NewPerson(name: "Ingrid Bergman")], 1, 20, 2)
        };
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/people");

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

    // ── GetAllPeople — pagination contract (#195, eight-case matrix) ────────

    [TestMethod]
    public async Task GetAllPeople_PageZero_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/people?page=0");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllPeople_PageMalformed_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/people?page=abc");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllPeople_PageSizeMalformed_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/people?pageSize=abc");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllPeople_PageSizeNegative_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/people?pageSize=-1");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAllPeople_PageSizeAbove500_Returns422NotSilentClamp()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/people?pageSize=999");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode, "pageSize above 500 must be rejected, not silently clamped");
    }

    [TestMethod]
    public async Task GetAllPeople_PageSizeZero_ReturnsAllRowsAsOnePage()
    {
        var repo = new FakePersonRepository
        {
            ReturnPage = new PagedItems<Person>([NewPerson(), NewPerson(name: "Ingrid Bergman"), NewPerson(name: "Claude Rains")], 1, 3, 3)
        };
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/people?pageSize=0");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(3, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.AreEqual(3, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.AreEqual(3, doc.RootElement.GetProperty("pageSize").GetInt32(), "pageSize=0 reports the effective count, not the literal 0 requested");
    }

    [TestMethod]
    public async Task GetAllPeople_PageSizeOmitted_DefaultsTo20()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/people");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(20, doc.RootElement.GetProperty("pageSize").GetInt32());
    }

    [TestMethod]
    public async Task GetAllPeople_PageBeyondLast_Returns422DistinctDetail()
    {
        var repo = new FakePersonRepository
        {
            ReturnPage = new PagedItems<Person>([NewPerson()], 1, 20, 1)
        };
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/people?page=5");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── GetPersonById ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetPersonById_ExistingId_ReturnsPerson()
    {
        var id   = Guid.NewGuid();
        var repo = new FakePersonRepository
        {
            ReturnById = NewPerson(id: id, name: "Humphrey Bogart", completeness: CompletenessStatus.Complete)
        };
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/people/{id}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.AreEqual(id.ToString("D").ToUpperInvariant(), root.GetProperty("id").GetString());
        Assert.AreEqual("Humphrey Bogart", root.GetProperty("name").GetString());

        // Response shape assertions (Step 2/3): dateOfBirth/dateOfDeath/completenessStatus must
        // serialize as plain JSON string values, never the raw SafeValue<T> {"raw":..,"parsed":..} shape.
        Assert.AreEqual(JsonValueKind.String, root.GetProperty("dateOfBirth").ValueKind);
        Assert.AreEqual("1899-12-25", root.GetProperty("dateOfBirth").GetString());
        Assert.AreEqual(JsonValueKind.String, root.GetProperty("dateOfDeath").ValueKind);
        Assert.AreEqual("1957-01-14", root.GetProperty("dateOfDeath").GetString());
        Assert.AreEqual(JsonValueKind.String, root.GetProperty("completenessStatus").ValueKind);
        Assert.AreEqual("Complete", root.GetProperty("completenessStatus").GetString());
    }

    [TestMethod]
    public async Task GetPersonById_UnknownDates_ReturnsNullNotEmptyString()
    {
        var id   = Guid.NewGuid();
        var repo = new FakePersonRepository
        {
            ReturnById = NewPerson(id: id, dateOfBirth: null, dateOfDeath: null)
        };
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/people/{id}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        AssertPropertyIsNullOrAbsent(root, "dateOfBirth");
        AssertPropertyIsNullOrAbsent(root, "dateOfDeath");
    }

    [TestMethod]
    public async Task GetPersonById_UnknownId_Returns404()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/people/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetPersonById_MalformedId_Returns404NotBadRequest()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/masterdata/people/not-a-guid");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetPersonById_LowercaseId_MatchesCaseInsensitively()
    {
        var id   = Guid.NewGuid();
        var repo = new FakePersonRepository { ReturnById = NewPerson(id: id) };
        using var factory = CreateFactory(repo);
        var response = await factory.CreateClient().GetAsync($"/api/v1/masterdata/people/{id.ToString("D").ToLowerInvariant()}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual(id.ToString("D").ToUpperInvariant(), root.GetProperty("id").GetString());
    }

    // ── OpenAPI: tag + rate limit, proven live ──────────────────────────────

    [TestMethod]
    public async Task PersonEndpoints_OnLiveSpec_TaggedMasterData()
    {
        using var factory = CreateFactory();
        var doc = await factory.CreateClient().GetFromJsonAsync<JsonDocument>("/openapi/v1.json");

        var paths = doc!.RootElement.GetProperty("paths");

        var listTags = paths.GetProperty("/api/v1/masterdata/people").GetProperty("get").GetProperty("tags");
        var byIdTags = paths.GetProperty("/api/v1/masterdata/people/{id}").GetProperty("get").GetProperty("tags");

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

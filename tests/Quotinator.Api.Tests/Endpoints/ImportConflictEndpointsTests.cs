using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Core.Models;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Engine.Models;
using Quotinator.Engine.Services;

namespace Quotinator.Api.Tests.Endpoints;

[TestClass]
public class ImportConflictEndpointsTests
{
    private const string TestKey = "test-admin-key";

    private static WebApplicationFactory<Program> CreateFactory(
        FakeConflictResolutionService? service = null,
        string? adminApiKey = TestKey)
    {
        var fakeService = service ?? new FakeConflictResolutionService();

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(new NoOpDatabaseInitializer());
                services.AddSingleton<ICallerContext>(new NoOpCallerContext());
                services.AddSingleton<IConflictResolutionService>(fakeService);
            });
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Quotinator:AdminApiKey"] = adminApiKey
                });
            });
        });
    }

    // ── GET /conflicts — public, no key required ─────────────────────────────

    [TestMethod]
    public async Task GetConflicts_NoApiKey_Returns200()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/import/conflicts");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task GetConflicts_ReturnsPageShape()
    {
        var fake = new FakeConflictResolutionService
        {
            ReturnPage = new ConflictPageResponse
            {
                TotalMatching = 1,
                TotalPages    = 1,
                Page          = 1,
                PageSize      = 50,
                Items =
                [
                    new ConflictSummaryResponse
                    {
                        Id             = Guid.NewGuid(),
                        EntityType     = "Quote",
                        Status         = "pending",
                        BatchId        = "BATCH-1",
                        SameFile       = false,
                        DetectedAt     = DateTime.UtcNow,
                        ExistingFields = new QuoteConflictFieldsDto(),
                        IncomingFields = new QuoteConflictFieldsDto(),
                    }
                ],
            }
        };
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/import/conflicts");
        var doc      = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.AreEqual(1, doc.RootElement.GetProperty("totalMatching").GetInt32());
        Assert.AreEqual(1, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    // ── POST /conflicts/{id}/decide — requires X-Api-Key ─────────────────────

    [TestMethod]
    public async Task DecideConflict_NoKey_Returns401()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/v1/import/conflicts/{Guid.NewGuid()}/decide", new ConflictDecisionRequest());

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task DecideConflict_CorrectKey_Returns204AndForwardsRequest()
    {
        var fake = new FakeConflictResolutionService();
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var conflictId = Guid.NewGuid();
        var request = new ConflictDecisionRequest { QuoteText = new FieldDecision { Choice = FieldResolutionChoice.Replace } };

        var response = await client.PostAsJsonAsync($"/api/v1/import/conflicts/{conflictId}/decide", request);

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        Assert.AreEqual(conflictId, fake.LastDecidedConflictId);
        Assert.AreEqual(FieldResolutionChoice.Replace, fake.LastDecisionRequest!.QuoteText!.Choice);
    }

    [TestMethod]
    public async Task DecideConflict_UnknownId_Returns404()
    {
        var fake = new FakeConflictResolutionService { ThrowOnDecide = new ConflictNotFoundException(Guid.NewGuid()) };
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.PostAsJsonAsync($"/api/v1/import/conflicts/{Guid.NewGuid()}/decide", new ConflictDecisionRequest());

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task DecideConflict_AmbiguousFieldUnresolved_Returns422WithFieldNames()
    {
        var fake = new FakeConflictResolutionService { ThrowOnDecide = new UnresolvedFieldConflictException(["genres", "source"]) };
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.PostAsJsonAsync($"/api/v1/import/conflicts/{Guid.NewGuid()}/decide", new ConflictDecisionRequest());
        var body     = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        StringAssert.Contains(body, "genres");
        StringAssert.Contains(body, "source");
    }

    [TestMethod]
    public async Task DecideConflict_AlreadyResolved_Returns422()
    {
        var fake = new FakeConflictResolutionService { ThrowOnDecide = new ConflictStateException(Guid.NewGuid(), ImportConflictStatus.Resolved) };
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.PostAsJsonAsync($"/api/v1/import/conflicts/{Guid.NewGuid()}/decide", new ConflictDecisionRequest());

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── POST /conflicts/{id}/undo — requires X-Api-Key ───────────────────────

    [TestMethod]
    public async Task UndoConflict_NoKey_Returns401()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.PostAsync($"/api/v1/import/conflicts/{Guid.NewGuid()}/undo", null);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task UndoConflict_CorrectKey_Returns204()
    {
        var fake = new FakeConflictResolutionService();
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var conflictId = Guid.NewGuid();
        var response = await client.PostAsync($"/api/v1/import/conflicts/{conflictId}/undo", null);

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        Assert.AreEqual(conflictId, fake.LastUndoneConflictId);
    }

    [TestMethod]
    public async Task UndoConflict_NotDecided_Returns422()
    {
        var fake = new FakeConflictResolutionService { ThrowOnUndo = new ConflictStateException(Guid.NewGuid(), ImportConflictStatus.Pending) };
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.PostAsync($"/api/v1/import/conflicts/{Guid.NewGuid()}/undo", null);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── POST /conflicts/apply — requires X-Api-Key ───────────────────────────

    [TestMethod]
    public async Task ApplyBatch_NoKey_Returns401()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.PostAsync("/api/v1/import/conflicts/apply?batchId=BATCH-1", null);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ApplyBatch_EveryConflictDecided_Returns200()
    {
        var fake = new FakeConflictResolutionService { ReturnApplyResult = null };
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.PostAsync("/api/v1/import/conflicts/apply?batchId=BATCH-1", null);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("BATCH-1", fake.LastAppliedBatchId);
    }

    [TestMethod]
    public async Task ApplyBatch_SomeConflictsStillPending_Returns422WithPendingIds()
    {
        var pendingId = Guid.NewGuid();
        var fake = new FakeConflictResolutionService
        {
            ReturnApplyResult = new ConflictBatchStatusResponse { BatchId = "BATCH-1", PendingConflictIds = [pendingId] }
        };
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.PostAsync("/api/v1/import/conflicts/apply?batchId=BATCH-1", null);
        var body     = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        StringAssert.Contains(body, pendingId.ToString());
    }
}

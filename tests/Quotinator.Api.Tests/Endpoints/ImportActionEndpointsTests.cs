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
using Quotinator.Data.Import;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Engine.Models;
using Quotinator.Engine.Services;

namespace Quotinator.Api.Tests.Endpoints;

[TestClass]
public class ImportActionEndpointsTests
{
    private const string TestKey = "test-admin-key";

    private static WebApplicationFactory<Program> CreateFactory(
        FakeImportActionService? service = null,
        string? adminApiKey = TestKey)
    {
        var fakeService = service ?? new FakeImportActionService();

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(new NoOpDatabaseInitializer());
                services.AddSingleton<ICallerContext>(new NoOpCallerContext());
                services.AddSingleton<IImportActionService>(fakeService);
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

    // ── GET /actions — public, no key required ───────────────────────────────

    [TestMethod]
    public async Task GetActions_NoApiKey_Returns200()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/import/actions");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task GetActions_ReturnsPageShape()
    {
        var fake = new FakeImportActionService
        {
            ReturnPage = new ImportActionPageResponse
            {
                TotalMatching = 1,
                TotalPages    = 1,
                Page          = 1,
                PageSize      = 50,
                Items =
                [
                    new ImportActionSummaryResponse
                    {
                        Id             = Guid.NewGuid(),
                        BatchId        = "BATCH-1",
                        ActionType     = "Modify",
                        EntityType     = "Quote",
                        EntityId       = "11111111-1111-1111-1111-111111111111",
                        Status         = "Pending",
                        DetectedAt     = DateTime.UtcNow,
                        IncomingFields = new Dictionary<string, object?>(),
                        AmbiguousFields = ["quoteText"],
                    }
                ],
            }
        };
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/import/actions");
        var doc      = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.AreEqual(1, doc.RootElement.GetProperty("totalMatching").GetInt32());
        Assert.AreEqual(1, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.AreEqual("quoteText", doc.RootElement.GetProperty("items")[0].GetProperty("ambiguousFields")[0].GetString());
    }

    // ── POST /actions/{id}/decide — requires X-Api-Key ───────────────────────

    [TestMethod]
    public async Task DecideAction_NoKey_Returns401()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/v1/import/actions/{Guid.NewGuid()}/decide", new ConflictDecisionRequest());

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task DecideAction_CorrectKey_Returns204AndForwardsRequest()
    {
        var fake = new FakeImportActionService();
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var actionId = Guid.NewGuid();
        var request = new ConflictDecisionRequest { QuoteText = new FieldDecision { Choice = FieldResolutionChoice.Replace } };

        var response = await client.PostAsJsonAsync($"/api/v1/import/actions/{actionId}/decide", request);

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        Assert.AreEqual(actionId, fake.LastDecidedActionId);
        Assert.AreEqual(FieldResolutionChoice.Replace, fake.LastDecisionRequest!.QuoteText!.Choice);
    }

    [TestMethod]
    public async Task DecideAction_UnknownId_Returns404()
    {
        var fake = new FakeImportActionService { ThrowOnDecide = new ImportActionNotFoundException(Guid.NewGuid()) };
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.PostAsJsonAsync($"/api/v1/import/actions/{Guid.NewGuid()}/decide", new ConflictDecisionRequest());

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task DecideAction_AmbiguousFieldUnresolved_Returns422WithFieldNames()
    {
        var fake = new FakeImportActionService { ThrowOnDecide = new UnresolvedFieldConflictException(["genres", "source"]) };
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.PostAsJsonAsync($"/api/v1/import/actions/{Guid.NewGuid()}/decide", new ConflictDecisionRequest());
        var body     = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        StringAssert.Contains(body, "genres");
        StringAssert.Contains(body, "source");
    }

    [TestMethod]
    public async Task DecideAction_AlreadyResolved_Returns422()
    {
        var fake = new FakeImportActionService { ThrowOnDecide = new ImportActionStateException(Guid.NewGuid(), "Applied") };
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.PostAsJsonAsync($"/api/v1/import/actions/{Guid.NewGuid()}/decide", new ConflictDecisionRequest());

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task DecideAction_NotDecidable_Returns422()
    {
        var fake = new FakeImportActionService { ThrowOnDecide = new ImportActionNotDecidableException(Guid.NewGuid(), "Source") };
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.PostAsJsonAsync($"/api/v1/import/actions/{Guid.NewGuid()}/decide", new ConflictDecisionRequest());
        var body     = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        StringAssert.Contains(body, "Source");
    }

    // ── POST /actions/{id}/undo — requires X-Api-Key ─────────────────────────

    [TestMethod]
    public async Task UndoAction_NoKey_Returns401()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.PostAsync($"/api/v1/import/actions/{Guid.NewGuid()}/undo", null);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task UndoAction_CorrectKey_Returns204()
    {
        var fake = new FakeImportActionService();
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var actionId = Guid.NewGuid();
        var response = await client.PostAsync($"/api/v1/import/actions/{actionId}/undo", null);

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        Assert.AreEqual(actionId, fake.LastUndoneActionId);
    }

    [TestMethod]
    public async Task UndoAction_NotDecided_Returns422()
    {
        var fake = new FakeImportActionService { ThrowOnUndo = new ImportActionStateException(Guid.NewGuid(), "Pending") };
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.PostAsync($"/api/v1/import/actions/{Guid.NewGuid()}/undo", null);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── POST /actions/apply — requires X-Api-Key ─────────────────────────────

    [TestMethod]
    public async Task ApplyBatch_NoKey_Returns401()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.PostAsync("/api/v1/import/actions/apply?batchId=BATCH-1", null);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ApplyBatch_EveryActionDecided_Returns200()
    {
        var fake = new FakeImportActionService { ReturnApplyResult = null };
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.PostAsync("/api/v1/import/actions/apply?batchId=BATCH-1", null);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("BATCH-1", fake.LastAppliedBatchId);
    }

    [TestMethod]
    public async Task ApplyBatch_SomeActionsStillPending_Returns422WithPendingIds()
    {
        var pendingId = Guid.NewGuid();
        var fake = new FakeImportActionService
        {
            ReturnApplyResult = new ImportActionBatchStatusResponse { BatchId = "BATCH-1", PendingActionIds = [pendingId] }
        };
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.PostAsync("/api/v1/import/actions/apply?batchId=BATCH-1", null);
        var body     = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        StringAssert.Contains(body, pendingId.ToString());
    }

    // ── POST /actions/discard — requires X-Api-Key ───────────────────────────

    [TestMethod]
    public async Task DiscardBatch_NoKey_Returns401()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.PostAsync("/api/v1/import/actions/discard?batchId=BATCH-1", null);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task DiscardBatch_CorrectKey_Returns204()
    {
        var fake = new FakeImportActionService();
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.PostAsync("/api/v1/import/actions/discard?batchId=BATCH-1", null);

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        Assert.AreEqual("BATCH-1", fake.LastDiscardedBatchId);
    }

    [TestMethod]
    public async Task DiscardBatch_InvalidState_Returns422()
    {
        var fake = new FakeImportActionService { ThrowOnDiscard = new ImportBatchStateException("BATCH-1", "has already been applied and cannot be discarded.") };
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.PostAsync("/api/v1/import/actions/discard?batchId=BATCH-1", null);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}

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
            ReturnPage = new PagedItems<ImportActionSummaryResponse>(
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
                Page: 1, PageSize: 50, TotalCount: 1)
        };
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/import/actions");
        var doc      = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.AreEqual(1, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.AreEqual(1, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.AreEqual("quoteText", doc.RootElement.GetProperty("items")[0].GetProperty("ambiguousFields")[0].GetString());
    }

    // ── Pagination contract (#195) ────────────────────────────────────────────

    [TestMethod]
    public async Task ImportActions_PageSizeAbove500_Returns422NotSilentClamp()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/import/actions?pageSize=999");

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode, "pageSize above 500 must be rejected, not silently clamped");
    }

    [TestMethod]
    public async Task ImportActions_PageSizeOmitted_DefaultsTo20NotFifty()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/import/actions");
        var doc      = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.AreEqual(20, doc.RootElement.GetProperty("pageSize").GetInt32(), "the standard shared default is 20, not import/actions' old default of 50");
    }

    [TestMethod]
    public async Task ImportActions_PageZero_Returns422()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/import/actions?page=0");

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task ImportActions_PageMalformed_Returns422()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/import/actions?page=abc");

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task ImportActions_PageSizeMalformed_Returns422()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/import/actions?pageSize=abc");

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task ImportActions_PageSizeNegative_Returns422()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/import/actions?pageSize=-1");

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task ImportActions_PageSizeZero_Succeeds()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/import/actions?pageSize=0");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, "pageSize=0 means every row as one page — must succeed, not 422");
    }

    [TestMethod]
    public async Task ImportActions_PageBeyondLast_Returns422()
    {
        var fake = new FakeImportActionService
        {
            ReturnPage = new PagedItems<ImportActionSummaryResponse>(
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
                Page: 1, PageSize: 1, TotalCount: 1)
        };
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/import/actions?page=5");

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode, "page beyond the last page must be rejected");
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

    /// <summary>
    /// #165: regression guard, found live via T2 — <c>CompletenessStatus</c> initially had no
    /// <c>[JsonConverter]</c>, so a real HTTP request with <c>"markCompletenessAs":"complete"</c>
    /// failed model binding with a bare 400 before ever reaching the service. Every other test here
    /// round-trips <see cref="ConflictDecisionRequest"/> via real <c>PostAsJsonAsync</c> JSON too, but
    /// none of them set a non-null <c>MarkCompletenessAs</c>, so none caught it.
    /// </summary>
    [TestMethod]
    public async Task DecideAction_WithMarkCompletenessAs_DeserializesAndForwards()
    {
        var fake = new FakeImportActionService();
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var actionId = Guid.NewGuid();
        var request = new ConflictDecisionRequest
        {
            SourceTitle = new FieldDecision { Choice = FieldResolutionChoice.Replace },
            MarkCompletenessAs = CompletenessStatus.Complete,
        };

        var response = await client.PostAsJsonAsync($"/api/v1/import/actions/{actionId}/decide", request);

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        Assert.AreEqual(CompletenessStatus.Complete, fake.LastDecisionRequest!.MarkCompletenessAs);
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
    public async Task ApplyBatch_MissingBatchId_Returns422NotGenericNumericFallback()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.PostAsync("/api/v1/import/actions/apply", null);
        var body     = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.IsFalse(body.Contains("Numeric parameters"), "must not fall through to the generic BadHttpRequestException safety-net message — batchId is not numeric");
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
    public async Task DiscardBatch_MissingBatchId_Returns422NotGenericNumericFallback()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.PostAsync("/api/v1/import/actions/discard", null);
        var body     = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.IsFalse(body.Contains("Numeric parameters"), "must not fall through to the generic BadHttpRequestException safety-net message — batchId is not numeric");
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

    // ── POST /actions/reverse — requires X-Api-Key ───────────────────────────

    [TestMethod]
    public async Task ReverseActions_NoApiKey_Returns401()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();

        var response = await client.PostAsync("/api/v1/import/actions/reverse?batchId=BATCH-1", null);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ReverseActions_MissingBatchId_Returns422NotGenericNumericFallback()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.PostAsync("/api/v1/import/actions/reverse", null);
        var body     = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.IsFalse(body.Contains("Numeric parameters"), "must not fall through to the generic BadHttpRequestException safety-net message — batchId is not numeric");
    }

    [TestMethod]
    public async Task ReverseActions_CorrectKey_Returns200()
    {
        var fake = new FakeImportActionService();
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.PostAsync("/api/v1/import/actions/reverse?batchId=BATCH-1", null);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("BATCH-1", fake.LastReversedBatchId);
        Assert.AreEqual(false, fake.LastReversePreview);
    }

    [TestMethod]
    public async Task ReverseActions_LowercaseBatchId_StillMatchesUppercaseStoredValue()
    {
        // The endpoint passes batchId straight through as a string — case-insensitive matching is
        // the service/coordinator's own responsibility (already covered at that layer). This proves
        // the endpoint itself does not mangle or reject a lowercase batchId before it gets there.
        var fake = new FakeImportActionService();
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.PostAsync("/api/v1/import/actions/reverse?batchId=batch-1", null);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("batch-1", fake.LastReversedBatchId);
    }

    [TestMethod]
    public async Task ReverseActions_Preview_PassesPreviewTrueAndReturns200()
    {
        var fake = new FakeImportActionService();
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.PostAsync("/api/v1/import/actions/reverse?batchId=BATCH-1&preview=true", null);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(true, fake.LastReversePreview);
    }

    [TestMethod]
    public async Task ReverseActions_UnknownOrAlreadyReversedBatchId_Returns404()
    {
        var fake = new FakeImportActionService { ThrowOnReverse = new ImportBatchNotFoundException(Guid.NewGuid()) };
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.PostAsync("/api/v1/import/actions/reverse?batchId=BATCH-1", null);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task ReverseActions_EmptyOrNotApplied_Returns422()
    {
        var fake = new FakeImportActionService { ThrowOnReverse = new ImportBatchStateException("BATCH-1", "has no actions and cannot be reversed.") };
        using var factory = CreateFactory(fake);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.PostAsync("/api/v1/import/actions/reverse?batchId=BATCH-1", null);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}

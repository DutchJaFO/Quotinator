using Quotinator.Data.Enums;
using System.Net;
using System.Net.Http.Headers;
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
        FakeImportActionService fakeService = service ?? new FakeImportActionService();

        return new QuotinatorWebApplicationFactory().WithWebHostBuilder(builder =>
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

    private static HttpClient CreateAuthorizedClient(WebApplicationFactory<Program> factory)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);
        return client;
    }

    // ── GET /actions — public, no key required ───────────────────────────────

    [TestMethod]
    public async Task GetActions_NoApiKey_Returns200()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/import/actions", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task GetActions_ReturnsPageShape()
    {
        FakeImportActionService fake = new()
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
        using WebApplicationFactory<Program> factory = CreateFactory(fake);
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/import/actions", TestContext.CancellationToken);
        JsonDocument doc      = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(1, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.AreEqual(1, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.AreEqual("quoteText", doc.RootElement.GetProperty("items")[0].GetProperty("ambiguousFields")[0].GetString());
    }

    // ── Pagination contract (#195) ────────────────────────────────────────────

    [TestMethod]
    public async Task ImportActions_PageSizeAbove500_Returns422NotSilentClamp()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/import/actions?pageSize=999", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode, "pageSize above 500 must be rejected, not silently clamped");
    }

    [TestMethod]
    public async Task ImportActions_PageSizeOmitted_DefaultsTo20NotFifty()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/import/actions", TestContext.CancellationToken);
        JsonDocument doc      = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(20, doc.RootElement.GetProperty("pageSize").GetInt32(), "the standard shared default is 20, not import/actions' old default of 50");
    }

    [TestMethod]
    public async Task ImportActions_PageZero_Returns422()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/import/actions?page=0", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task ImportActions_PageMalformed_Returns422()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/import/actions?page=abc", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task ImportActions_PageSizeMalformed_Returns422()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/import/actions?pageSize=abc", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task ImportActions_PageSizeNegative_Returns422()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/import/actions?pageSize=-1", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task ImportActions_PageSizeZero_Succeeds()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/import/actions?pageSize=0", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, "pageSize=0 means every row as one page — must succeed, not 422");
    }

    [TestMethod]
    public async Task ImportActions_PageBeyondLast_Returns422()
    {
        FakeImportActionService fake = new()
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
        using WebApplicationFactory<Program> factory = CreateFactory(fake);
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/import/actions?page=5", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode, "page beyond the last page must be rejected");
    }

    // ── GET /actions/export — public, no key required (#163) ─────────────────

    [TestMethod]
    public async Task ExportActions_NoApiKey_Returns200()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/import/actions/export?batchId=BATCH-1", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task ExportActions_BatchIdMissing_Returns422()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/import/actions/export", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task ExportActions_UnknownFormat_Returns422()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/import/actions/export?batchId=BATCH-1&format=xml", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task ExportActions_DefaultFormat_ReturnsJsonRows()
    {
        Guid actionId = Guid.NewGuid();
        FakeImportActionService fake = new()
        {
            ReturnExportRows =
            [
                new ImportActionFieldRowResponse
                {
                    ActionId      = actionId,
                    EntityId      = "e0000001-0000-4000-8000-000000000001",
                    EntityType    = "Person",
                    Field         = "name",
                    ExistingValue = "Old Name",
                    IncomingValue = "New Name",
                },
            ],
        };
        using WebApplicationFactory<Program> factory = CreateFactory(fake);
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/import/actions/export?batchId=BATCH-1", TestContext.CancellationToken);
        List<ImportActionFieldRowResponse>? rows = await response.Content.ReadFromJsonAsync<List<ImportActionFieldRowResponse>>(TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(rows);
        Assert.HasCount(1, rows);
        Assert.AreEqual("name", rows[0].Field);
        Assert.AreEqual("BATCH-1", fake.LastExportedBatchId);
    }

    [TestMethod]
    public async Task ExportActions_CsvFormat_ReturnsCsvWithHeaderAndDataRow()
    {
        FakeImportActionService fake = new()
        {
            ReturnExportRows =
            [
                new ImportActionFieldRowResponse
                {
                    ActionId      = Guid.NewGuid(),
                    EntityId      = "e0000001-0000-4000-8000-000000000001",
                    EntityType    = "Person",
                    Field         = "name",
                    ExistingValue = "Old Name",
                    IncomingValue = "New Name",
                },
            ],
        };
        using WebApplicationFactory<Program> factory = CreateFactory(fake);
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/import/actions/export?batchId=BATCH-1&format=csv", TestContext.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("text/csv", response.Content.Headers.ContentType?.MediaType);
        string[] lines = body.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual("ActionId,EntityId,EntityType,Field,ExistingValue,IncomingValue,Decision,CustomValue,MarkCompletenessAs", lines[0]);
        Assert.IsTrue(lines[1].Contains("Old Name") && lines[1].Contains("New Name"));
    }

    // ── POST /actions/bulk-decide — requires X-Api-Key (#163) ────────────────

    private static MultipartFormDataContent BuildBulkDecideForm(string fileContent, bool includeFile = true)
    {
        MultipartFormDataContent form = [];
        if (includeFile)
        {
            ByteArrayContent part = new(System.Text.Encoding.UTF8.GetBytes(fileContent));
            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(part, "file", "bulk-decide.json");
        }
        else
        {
            // A genuinely zero-part MultipartFormDataContent serializes to a body ReadFormAsync
            // rejects as malformed ("Form section has invalid Content-Disposition value") — the same
            // workaround ImportEndpointTests.cs's own BuildForm helper already uses for the identical
            // reason.
            form.Add(new StringContent(string.Empty), "_empty");
        }
        return form;
    }

    [TestMethod]
    public async Task BulkDecide_NoApiKey_Returns401()
    {
        using WebApplicationFactory<Program> factory = CreateFactory(adminApiKey: TestKey);
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/actions/bulk-decide?batchId=BATCH-1", BuildBulkDecideForm("[]"), TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task BulkDecide_BatchIdMissing_Returns422()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = CreateAuthorizedClient(factory);

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/actions/bulk-decide", BuildBulkDecideForm("[]"), TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task BulkDecide_FileMissing_Returns422()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = CreateAuthorizedClient(factory);

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/actions/bulk-decide?batchId=BATCH-1", BuildBulkDecideForm("", includeFile: false), TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task BulkDecide_NoBodyAndNoBatchId_Returns422()
    {
        // Mirrors POST /import's own bodyless-request fix (see ImportEndpoints.cs's HttpRequest
        // parameter comment): minimal API's automatic IFormFile?/[FromForm] binding fails at the
        // framework's routing layer — not via a thrown exception — for a request with no form
        // content-type/body at all, producing a bare, uninformative 400 that bypasses this
        // endpoint's own batchId/file validation entirely. Found live via T2 Docker testing, where
        // a bare `curl -X POST .../bulk-decide` (no -F flags at all) returned 400 instead of the
        // expected 422.
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = CreateAuthorizedClient(factory);

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/actions/bulk-decide", content: null, TestContext.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("batchId", body,
            "Must be a clear detail message naming batchId, not a bare framework 400 with no detail.");
    }

    [TestMethod]
    public async Task BulkDecide_NoBodyButBatchIdPresent_Returns422ForMissingFile()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = CreateAuthorizedClient(factory);

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/actions/bulk-decide?batchId=BATCH-1", content: null, TestContext.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("file", body,
            "Once batchId is present, a bodyless request must still be reported as a missing file, not a bare 400.");
    }

    [TestMethod]
    public async Task BulkDecide_UnknownFormat_Returns422()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = CreateAuthorizedClient(factory);

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/actions/bulk-decide?batchId=BATCH-1&format=xml", BuildBulkDecideForm("[]"), TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task BulkDecide_ValidJsonRows_CallsServiceAndReturnsResponse()
    {
        Guid actionId = Guid.NewGuid();
        FakeImportActionService fake = new()
        {
            ReturnBulkDecideResponse = new BulkDecideResponse { RowsProcessed = 1, ActionsDecided = 1 },
        };
        using WebApplicationFactory<Program> factory = CreateFactory(fake);
        using HttpClient client  = CreateAuthorizedClient(factory);

        string json = JsonSerializer.Serialize(new[]
        {
            new { ActionId = actionId, EntityId = "e0000001-0000-4000-8000-000000000001", EntityType = "Person", Field = "name", Decision = "Replace" },
        });

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/actions/bulk-decide?batchId=BATCH-1", BuildBulkDecideForm(json), TestContext.CancellationToken);
        BulkDecideResponse? body = await response.Content.ReadFromJsonAsync<BulkDecideResponse>(TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(1, body!.ActionsDecided);
        Assert.AreEqual("BATCH-1", fake.LastBulkDecidedBatchId);
        Assert.IsNotNull(fake.LastBulkDecideRows);
        Assert.HasCount(1, fake.LastBulkDecideRows);
        Assert.AreEqual(actionId, fake.LastBulkDecideRows[0].ActionId);
    }

    /// <summary>
    /// Found live via T2 (2026-07-25): re-submitting <c>GET /import/actions/export</c>'s own JSON
    /// output verbatim — the exact round trip the endpoint exists for — failed every row with "missing
    /// required properties" despite the data being present. The app-wide JSON config
    /// (<c>ConfigureHttpJsonOptions</c> in Program.cs) serializes export's response in ASP.NET's default
    /// camelCase, but the endpoint's own request-body parsing used <c>element.Deserialize&lt;T&gt;()</c>
    /// with no explicit options, which falls back to <see cref="JsonSerializer"/>'s library default
    /// (case-sensitive, PascalCase-only) — the exact mismatch this test forces by hand-typing camelCase
    /// property names, matching what a real client re-submitting the export actually sends.
    /// </summary>
    [TestMethod]
    public async Task BulkDecide_CamelCaseJsonPropertyNames_MatchingExportsOwnOutput_ParsesSuccessfully()
    {
        Guid actionId = Guid.NewGuid();
        FakeImportActionService fake = new()
        {
            ReturnBulkDecideResponse = new BulkDecideResponse { RowsProcessed = 1, ActionsDecided = 1 },
        };
        using WebApplicationFactory<Program> factory = CreateFactory(fake);
        using HttpClient client  = CreateAuthorizedClient(factory);

        string json = $$"""
            [{"actionId":"{{actionId}}","entityId":"e0000001-0000-4000-8000-000000000001","entityType":"Person","field":"name","existingValue":"Old","incomingValue":"New","decision":"Replace"}]
            """;

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/actions/bulk-decide?batchId=BATCH-1", BuildBulkDecideForm(json), TestContext.CancellationToken);
        BulkDecideResponse? body = await response.Content.ReadFromJsonAsync<BulkDecideResponse>(TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsEmpty(body!.Errors, "camelCase property names (export's own output shape) must parse without error");
        Assert.IsNotNull(fake.LastBulkDecideRows);
        Assert.HasCount(1, fake.LastBulkDecideRows);
        Assert.AreEqual(actionId, fake.LastBulkDecideRows[0].ActionId);
        Assert.AreEqual(FieldResolutionChoice.Replace, fake.LastBulkDecideRows[0].Decision);
    }

    [TestMethod]
    public async Task BulkDecide_MalformedJsonRow_ReportedAsErrorWithoutAbortingValidRows()
    {
        Guid validActionId = Guid.NewGuid();
        FakeImportActionService fake = new()
        {
            ReturnBulkDecideResponse = new BulkDecideResponse { RowsProcessed = 1, ActionsDecided = 1 },
        };
        using WebApplicationFactory<Program> factory = CreateFactory(fake);
        using HttpClient client  = CreateAuthorizedClient(factory);

        // First element has an unrecognised Decision value; second is well-formed.
        string json = $$"""
            [
              {"ActionId":"{{Guid.NewGuid()}}","EntityId":"e0000001-0000-4000-8000-000000000001","EntityType":"Person","Field":"name","Decision":"NotARealChoice"},
              {"ActionId":"{{validActionId}}","EntityId":"e0000001-0000-4000-8000-000000000001","EntityType":"Person","Field":"name","Decision":"Replace"}
            ]
            """;

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/actions/bulk-decide?batchId=BATCH-1", BuildBulkDecideForm(json), TestContext.CancellationToken);
        BulkDecideResponse? body = await response.Content.ReadFromJsonAsync<BulkDecideResponse>(TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.HasCount(1, body!.Errors, "The malformed row must be reported as an error");
        Assert.IsNotNull(fake.LastBulkDecideRows);
        Assert.HasCount(1, fake.LastBulkDecideRows, "Only the well-formed row reaches the service");
        Assert.AreEqual(validActionId, fake.LastBulkDecideRows[0].ActionId);
    }

    [TestMethod]
    public async Task BulkDecide_CsvFormat_ParsesRowsAndCallsService()
    {
        Guid actionId = Guid.NewGuid();
        FakeImportActionService fake = new()
        {
            ReturnBulkDecideResponse = new BulkDecideResponse { RowsProcessed = 1, ActionsDecided = 1 },
        };
        using WebApplicationFactory<Program> factory = CreateFactory(fake);
        using HttpClient client  = CreateAuthorizedClient(factory);

        string csv = "ActionId,EntityId,EntityType,Field,ExistingValue,IncomingValue,Decision,CustomValue,MarkCompletenessAs\r\n" +
                  $"{actionId},e0000001-0000-4000-8000-000000000001,Person,name,Old Name,New Name,Replace,,\r\n";

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/actions/bulk-decide?batchId=BATCH-1&format=csv", BuildBulkDecideForm(csv), TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(fake.LastBulkDecideRows);
        Assert.HasCount(1, fake.LastBulkDecideRows);
        Assert.AreEqual(actionId, fake.LastBulkDecideRows[0].ActionId);
        Assert.AreEqual(FieldResolutionChoice.Replace, fake.LastBulkDecideRows[0].Decision);
    }

    // ── POST /actions/{id}/decide — requires X-Api-Key ───────────────────────

    [TestMethod]
    public async Task DecideAction_NoKey_Returns401()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync($"/api/v1/import/actions/{Guid.NewGuid()}/decide", new ConflictDecisionRequest(), cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task DecideAction_CorrectKey_Returns204AndForwardsRequest()
    {
        FakeImportActionService fake = new();
        using WebApplicationFactory<Program> factory = CreateFactory(fake);
        using HttpClient client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        Guid actionId = Guid.NewGuid();
        ConflictDecisionRequest request = new() { QuoteText = new FieldDecision { Choice = FieldResolutionChoice.Replace } };

        HttpResponseMessage response = await client.PostAsJsonAsync($"/api/v1/import/actions/{actionId}/decide", request, cancellationToken: TestContext.CancellationToken);

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
        FakeImportActionService fake = new();
        using WebApplicationFactory<Program> factory = CreateFactory(fake);
        using HttpClient client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        Guid actionId = Guid.NewGuid();
        ConflictDecisionRequest request = new()
        {
            SourceTitle = new FieldDecision { Choice = FieldResolutionChoice.Replace },
            MarkCompletenessAs = CompletenessStatus.Complete,
        };

        HttpResponseMessage response = await client.PostAsJsonAsync($"/api/v1/import/actions/{actionId}/decide", request, cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        Assert.AreEqual(CompletenessStatus.Complete, fake.LastDecisionRequest!.MarkCompletenessAs);
    }

    [TestMethod]
    public async Task DecideAction_UnknownId_Returns404()
    {
        FakeImportActionService fake = new() { DecideResult = ImportActionDecideResult.NotFound };
        using WebApplicationFactory<Program> factory = CreateFactory(fake);
        using HttpClient client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        HttpResponseMessage response = await client.PostAsJsonAsync($"/api/v1/import/actions/{Guid.NewGuid()}/decide", new ConflictDecisionRequest(), cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task DecideAction_AmbiguousFieldUnresolved_Returns422WithFieldNames()
    {
        FakeImportActionService fake = new() { DecideResult = id => ImportActionDecideResult.Unresolved(id, ["genres", "source"]) };
        using WebApplicationFactory<Program> factory = CreateFactory(fake);
        using HttpClient client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        HttpResponseMessage response = await client.PostAsJsonAsync($"/api/v1/import/actions/{Guid.NewGuid()}/decide", new ConflictDecisionRequest(), cancellationToken: TestContext.CancellationToken);
        string body     = await response.Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("genres", body);
        Assert.Contains("source", body);
    }

    [TestMethod]
    public async Task DecideAction_AlreadyResolved_Returns422()
    {
        FakeImportActionService fake = new() { DecideResult = id => ImportActionDecideResult.AlreadyResolved(id, "Applied") };
        using WebApplicationFactory<Program> factory = CreateFactory(fake);
        using HttpClient client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        HttpResponseMessage response = await client.PostAsJsonAsync($"/api/v1/import/actions/{Guid.NewGuid()}/decide", new ConflictDecisionRequest(), cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task DecideAction_NotDecidable_Returns422()
    {
        FakeImportActionService fake = new() { DecideResult = id => ImportActionDecideResult.NotDecidable(id, "Source", "Add") };
        using WebApplicationFactory<Program> factory = CreateFactory(fake);
        using HttpClient client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        HttpResponseMessage response = await client.PostAsJsonAsync($"/api/v1/import/actions/{Guid.NewGuid()}/decide", new ConflictDecisionRequest(), cancellationToken: TestContext.CancellationToken);
        string body     = await response.Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("Source", body);
    }

    // ── POST /actions/{id}/undo — requires X-Api-Key ─────────────────────────

    [TestMethod]
    public async Task UndoAction_NoKey_Returns401()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync($"/api/v1/import/actions/{Guid.NewGuid()}/undo", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task UndoAction_CorrectKey_Returns204()
    {
        FakeImportActionService fake = new();
        using WebApplicationFactory<Program> factory = CreateFactory(fake);
        using HttpClient client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        Guid actionId = Guid.NewGuid();
        HttpResponseMessage response = await client.PostAsync($"/api/v1/import/actions/{actionId}/undo", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        Assert.AreEqual(actionId, fake.LastUndoneActionId);
    }

    [TestMethod]
    public async Task UndoAction_NotDecided_Returns422()
    {
        FakeImportActionService fake = new() { ThrowOnUndo = new ImportActionStateException(Guid.NewGuid(), "Pending") };
        using WebApplicationFactory<Program> factory = CreateFactory(fake);
        using HttpClient client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        HttpResponseMessage response = await client.PostAsync($"/api/v1/import/actions/{Guid.NewGuid()}/undo", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── POST /actions/apply — requires X-Api-Key ─────────────────────────────

    [TestMethod]
    public async Task ApplyBatch_NoKey_Returns401()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/actions/apply?batchId=BATCH-1", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ApplyBatch_MissingBatchId_Returns422NotGenericNumericFallback()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/actions/apply", null, TestContext.CancellationToken);
        string body     = await response.Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.DoesNotContain("Numeric parameters", body, "must not fall through to the generic BadHttpRequestException safety-net message — batchId is not numeric");
    }

    [TestMethod]
    public async Task ApplyBatch_EveryActionDecided_Returns200()
    {
        FakeImportActionService fake = new() { ReturnApplyResult = null };
        using WebApplicationFactory<Program> factory = CreateFactory(fake);
        using HttpClient client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/actions/apply?batchId=BATCH-1", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("BATCH-1", fake.LastAppliedBatchId);
    }

    // ── POST /actions/apply — purgeOnSuccess (#249) ──────────────────────────

    [TestMethod]
    public async Task ApplyBatch_PurgeOnSuccessTrue_ForwardsTrueToService()
    {
        FakeImportActionService fake = new() { ReturnApplyResult = null };
        using WebApplicationFactory<Program> factory = CreateFactory(fake);
        using HttpClient client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/actions/apply?batchId=BATCH-1&purgeOnSuccess=true", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(fake.LastApplyPurgeOnSuccess);
    }

    [TestMethod]
    public async Task ApplyBatch_PurgeOnSuccessOmitted_ForwardsFalseToService()
    {
        FakeImportActionService fake = new() { ReturnApplyResult = null };
        using WebApplicationFactory<Program> factory = CreateFactory(fake);
        using HttpClient client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/actions/apply?batchId=BATCH-1", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsFalse(fake.LastApplyPurgeOnSuccess, "must default to false, not purge unless the caller explicitly opts in");
    }

    [TestMethod]
    public async Task ApplyBatch_PurgeOnSuccessTrue_BatchStillPending_DoesNotAffect422Outcome()
    {
        Guid pendingId = Guid.NewGuid();
        FakeImportActionService fake = new()
        {
            ReturnApplyResult = new ImportActionBatchStatusResponse { BatchId = "BATCH-1", PendingActionIds = [pendingId] }
        };
        using WebApplicationFactory<Program> factory = CreateFactory(fake);
        using HttpClient client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/actions/apply?batchId=BATCH-1&purgeOnSuccess=true", null, TestContext.CancellationToken);
        string body     = await response.Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains(pendingId.ToString(), body);
    }

    [TestMethod]
    public async Task ApplyBatch_SomeActionsStillPending_Returns422WithPendingIds()
    {
        Guid pendingId = Guid.NewGuid();
        FakeImportActionService fake = new()
        {
            ReturnApplyResult = new ImportActionBatchStatusResponse { BatchId = "BATCH-1", PendingActionIds = [pendingId] }
        };
        using WebApplicationFactory<Program> factory = CreateFactory(fake);
        using HttpClient client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/actions/apply?batchId=BATCH-1", null, TestContext.CancellationToken);
        string body     = await response.Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains(pendingId.ToString(), body);
    }

    // ── POST /actions/discard — requires X-Api-Key ───────────────────────────

    [TestMethod]
    public async Task DiscardBatch_NoKey_Returns401()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/actions/discard?batchId=BATCH-1", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task DiscardBatch_MissingBatchId_Returns422NotGenericNumericFallback()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/actions/discard", null, TestContext.CancellationToken);
        string body     = await response.Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.DoesNotContain("Numeric parameters", body, "must not fall through to the generic BadHttpRequestException safety-net message — batchId is not numeric");
    }

    [TestMethod]
    public async Task DiscardBatch_CorrectKey_Returns204()
    {
        FakeImportActionService fake = new();
        using WebApplicationFactory<Program> factory = CreateFactory(fake);
        using HttpClient client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/actions/discard?batchId=BATCH-1", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        Assert.AreEqual("BATCH-1", fake.LastDiscardedBatchId);
    }

    [TestMethod]
    public async Task DiscardBatch_InvalidState_Returns422()
    {
        FakeImportActionService fake = new() { ThrowOnDiscard = new ImportBatchStateException("BATCH-1", "has already been applied and cannot be discarded.") };
        using WebApplicationFactory<Program> factory = CreateFactory(fake);
        using HttpClient client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/actions/discard?batchId=BATCH-1", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── POST /actions/reverse — requires X-Api-Key ───────────────────────────

    [TestMethod]
    public async Task ReverseActions_NoApiKey_Returns401()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/actions/reverse?batchId=BATCH-1", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ReverseActions_MissingBatchId_Returns422NotGenericNumericFallback()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/actions/reverse", null, TestContext.CancellationToken);
        string body     = await response.Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.DoesNotContain("Numeric parameters", body, "must not fall through to the generic BadHttpRequestException safety-net message — batchId is not numeric");
    }

    [TestMethod]
    public async Task ReverseActions_CorrectKey_Returns200()
    {
        FakeImportActionService fake = new();
        using WebApplicationFactory<Program> factory = CreateFactory(fake);
        using HttpClient client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/actions/reverse?batchId=BATCH-1", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("BATCH-1", fake.LastReversedBatchId);
        Assert.IsFalse(fake.LastReversePreview);
    }

    [TestMethod]
    public async Task ReverseActions_LowercaseBatchId_StillMatchesUppercaseStoredValue()
    {
        // The endpoint passes batchId straight through as a string — case-insensitive matching is
        // the service/coordinator's own responsibility (already covered at that layer). This proves
        // the endpoint itself does not mangle or reject a lowercase batchId before it gets there.
        FakeImportActionService fake = new();
        using WebApplicationFactory<Program> factory = CreateFactory(fake);
        using HttpClient client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/actions/reverse?batchId=batch-1", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("batch-1", fake.LastReversedBatchId);
    }

    [TestMethod]
    public async Task ReverseActions_Preview_PassesPreviewTrueAndReturns200()
    {
        FakeImportActionService fake = new();
        using WebApplicationFactory<Program> factory = CreateFactory(fake);
        using HttpClient client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/actions/reverse?batchId=BATCH-1&preview=true", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(fake.LastReversePreview);
    }

    [TestMethod]
    public async Task ReverseActions_UnknownOrAlreadyReversedBatchId_Returns404()
    {
        FakeImportActionService fake = new() { ThrowOnReverse = new ImportBatchNotFoundException(Guid.NewGuid()) };
        using WebApplicationFactory<Program> factory = CreateFactory(fake);
        using HttpClient client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/actions/reverse?batchId=BATCH-1", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task ReverseActions_EmptyOrNotApplied_Returns422()
    {
        FakeImportActionService fake = new() { ThrowOnReverse = new ImportBatchStateException("BATCH-1", "has no actions and cannot be reversed.") };
        using WebApplicationFactory<Program> factory = CreateFactory(fake);
        using HttpClient client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        HttpResponseMessage response = await client.PostAsync("/api/v1/import/actions/reverse?batchId=BATCH-1", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    public TestContext TestContext { get; set; }
}

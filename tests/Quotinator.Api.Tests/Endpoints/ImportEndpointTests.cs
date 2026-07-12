using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Core.Services;
using Quotinator.Data.Import;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Engine.Services;

namespace Quotinator.Api.Tests.Endpoints;

[TestClass]
public class ImportEndpointTests
{
    private const string TestKey = "test-admin-key";

    private static WebApplicationFactory<Program> CreateFactory(
        string? adminApiKey, FakeQuoteImportService importService) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton(NoOpDatabaseInitializer.Instance);
                services.AddSingleton<ISystemAuditWriter>(new NoOpSystemAuditWriter());
                services.AddSingleton<ISystemAuditReader>(new NoOpSystemAuditReader());
                services.AddSingleton<ICallerContext>(new NoOpCallerContext());
                services.AddSingleton<IQuoteImportService>(importService);
            });

            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Quotinator:AdminApiKey"] = adminApiKey
                });
            });
        });

    private static HttpClient CreateClientWithKey(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", TestKey);
        return client;
    }

    private static MultipartFormDataContent BuildForm(string fileContent = "[]", string? settingsJson = null, bool includeFile = true)
    {
        var form = new MultipartFormDataContent();
        if (includeFile)
        {
            var fileContentPart = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(fileContent));
            fileContentPart.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            form.Add(fileContentPart, "file", "quotes.json");
        }
        if (settingsJson is not null)
            form.Add(new StringContent(settingsJson), "settings");
        if (!includeFile && settingsJson is null)
            form.Add(new StringContent(string.Empty), "_empty");
        return form;
    }

    [TestMethod]
    [DataRow("/api/v1/import")]
    [DataRow("/api/v1/import/preview")]
    public async Task Import_NoKeyConfigured_Returns401(string path)
    {
        using var factory = CreateFactory(null, new FakeQuoteImportService());
        var response = await factory.CreateClient().PostAsync(path, BuildForm());
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    [DataRow("/api/v1/import")]
    [DataRow("/api/v1/import/preview")]
    public async Task Import_MissingAuthHeader_Returns401(string path)
    {
        using var factory = CreateFactory(TestKey, new FakeQuoteImportService());
        var response = await factory.CreateClient().PostAsync(path, BuildForm());
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Import_MissingFile_Returns422()
    {
        using var factory = CreateFactory(TestKey, new FakeQuoteImportService());
        var response = await CreateClientWithKey(factory).PostAsync("/api/v1/import", BuildForm(includeFile: false));
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task Import_NoBodyAndNoBatchId_Returns422()
    {
        using var factory = CreateFactory(TestKey, new FakeQuoteImportService());
        var response = await CreateClientWithKey(factory).PostAsync("/api/v1/import", content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        StringAssert.Contains(body, "file to import or a batchId",
            "Must be the specific file-or-batchId message, not the generic numeric-parameters fallback " +
            "a bodyless request without this fix would otherwise fall through to under a real Kestrel host.");
    }

    [TestMethod]
    public async Task Import_MalformedSettingsJson_Returns422()
    {
        using var factory = CreateFactory(TestKey, new FakeQuoteImportService());
        var response = await CreateClientWithKey(factory)
            .PostAsync("/api/v1/import", BuildForm(settingsJson: "{ not json"));
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task Import_EnrichTrue_Returns501_BeforeCallingService()
    {
        var service = new FakeQuoteImportService();
        using var factory = CreateFactory(TestKey, service);
        var response = await CreateClientWithKey(factory)
            .PostAsync("/api/v1/import", BuildForm(settingsJson: """{"enrich":true}"""));

        Assert.AreEqual(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.IsNull(service.LastFileName, "The service must never be called when enrich=true short-circuits first");
    }

    [TestMethod]
    public async Task Import_UnknownConverter_Returns422()
    {
        var service = new FakeQuoteImportService { ThrowOnImport = new UnknownConverterException("bogus") };
        using var factory = CreateFactory(TestKey, service);
        var response = await CreateClientWithKey(factory)
            .PostAsync("/api/v1/import", BuildForm(settingsJson: """{"converter":"bogus"}"""));

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task Import_ServiceThrowsValidationException_Returns422()
    {
        var service = new FakeQuoteImportService { ThrowOnImport = new QuoteImportValidationException("File contained no quotes.") };
        using var factory = CreateFactory(TestKey, service);
        var response = await CreateClientWithKey(factory).PostAsync("/api/v1/import", BuildForm());

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task Import_CorrectKeyAndValidFile_Returns200WithResultShape()
    {
        using var factory = CreateFactory(TestKey, new FakeQuoteImportService());
        var response = await CreateClientWithKey(factory).PostAsync("/api/v1/import", BuildForm());

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(doc.RootElement.TryGetProperty("summary", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("preview", out var preview));
        Assert.IsFalse(preview.GetBoolean());
        Assert.AreEqual(0, doc.RootElement.GetProperty("conflicts").GetArrayLength(), "A zero-conflict import (the FakeQuoteImportService default) must report an empty conflicts array");
    }

    [TestMethod]
    [DataRow("/api/v1/import")]
    [DataRow("/api/v1/import/preview")]
    public async Task Import_ResultHasPendingConflict_Returns202(string path)
    {
        var service = new FakeQuoteImportService
        {
            ReturnResult = new Quotinator.Core.Models.ImportResultResponse
            {
                BatchId        = Guid.NewGuid(),
                Preview        = path.EndsWith("preview"),
                ConflictPolicy = "review",
                Summary        = new Quotinator.Core.Models.ImportSummary { Total = 1, Imported = 0, Updated = 0, Skipped = 1, Errors = 0 },
                Conflicts =
                [
                    new Quotinator.Core.Models.ImportConflictEntry
                    {
                        QuoteId       = "11111111-1111-1111-1111-111111111111",
                        AppliedPolicy = "review",
                        Status        = "pending",
                        ExistingValue = new Dictionary<string, object?>(),
                        IncomingValue = new Dictionary<string, object?>(),
                    }
                ],
                PendingActionIds = [Guid.NewGuid()],
            }
        };
        using var factory = CreateFactory(TestKey, service);
        var response = await CreateClientWithKey(factory).PostAsync(path, BuildForm());

        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
    }

    /// <summary>
    /// #165 regression guard, found live via T2: a batch held by a <c>Blocked</c> Source action
    /// produces an empty <c>Conflicts</c> list (that field only ever covered Quote Modify actions),
    /// so checking <c>Conflicts</c> alone silently reported <c>200</c> even though nothing in the
    /// batch had actually applied. <c>PendingActionIds</c> is the fix — populated from every held
    /// action regardless of entity type, so it must drive the status code on its own, independent of
    /// <c>Conflicts</c>.
    /// </summary>
    [TestMethod]
    [DataRow("/api/v1/import")]
    [DataRow("/api/v1/import/preview")]
    public async Task Import_PendingActionIdsNonEmptyButConflictsEmpty_Returns202(string path)
    {
        var service = new FakeQuoteImportService
        {
            ReturnResult = new Quotinator.Core.Models.ImportResultResponse
            {
                BatchId          = Guid.NewGuid(),
                Preview          = path.EndsWith("preview"),
                ConflictPolicy   = "newest-wins",
                Summary          = new Quotinator.Core.Models.ImportSummary { Total = 1, Imported = 1, Updated = 0, Skipped = 0, Errors = 0 },
                Conflicts        = [],
                PendingActionIds = [Guid.NewGuid()],
            }
        };
        using var factory = CreateFactory(TestKey, service);
        var response = await CreateClientWithKey(factory).PostAsync(path, BuildForm());

        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
    }

    [TestMethod]
    [DataRow("/api/v1/import")]
    [DataRow("/api/v1/import/preview")]
    public async Task Import_ResultHasOnlyResolvedConflicts_Returns200NotAccepted(string path)
    {
        var service = new FakeQuoteImportService
        {
            ReturnResult = new Quotinator.Core.Models.ImportResultResponse
            {
                BatchId        = Guid.NewGuid(),
                Preview        = path.EndsWith("preview"),
                ConflictPolicy = "newest-wins",
                Summary        = new Quotinator.Core.Models.ImportSummary { Total = 1, Imported = 0, Updated = 1, Skipped = 0, Errors = 0 },
                Conflicts =
                [
                    new Quotinator.Core.Models.ImportConflictEntry
                    {
                        QuoteId       = "11111111-1111-1111-1111-111111111111",
                        AppliedPolicy = "newest-wins",
                        Status        = "resolved",
                        ExistingValue = new Dictionary<string, object?>(),
                        IncomingValue = new Dictionary<string, object?>(),
                    }
                ],
            }
        };
        using var factory = CreateFactory(TestKey, service);
        var response = await CreateClientWithKey(factory).PostAsync(path, BuildForm());

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "A non-empty conflicts list must NOT force a 202 by itself -- only a genuinely 'pending' entry should. " +
            "An auto-resolved conflict (e.g. NewestWins) is reported for visibility but never blocks the import.");
    }

    [TestMethod]
    public async Task ImportPreview_CorrectKeyAndValidFile_Returns200WithPreviewTrue()
    {
        var service = new FakeQuoteImportService();
        using var factory = CreateFactory(TestKey, service);
        var response = await CreateClientWithKey(factory).PostAsync("/api/v1/import/preview", BuildForm());

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(true, service.LastPreview);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(doc.RootElement.GetProperty("preview").GetBoolean());
    }

    // ── batchId mode — POST /import?batchId= (alias for /import/actions/apply) ──────────────

    [TestMethod]
    public async Task Import_WithBatchId_CallsApplyStagedBatchAsyncNotImportAsync()
    {
        var service = new FakeQuoteImportService();
        using var factory = CreateFactory(TestKey, service);
        var batchId = Guid.NewGuid();

        var response = await CreateClientWithKey(factory)
            .PostAsync($"/api/v1/import?batchId={batchId}", BuildForm(includeFile: false));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(batchId, service.LastAppliedBatchId);
        Assert.IsNull(service.LastFileName, "batchId mode must never call the file-upload path");
    }

    [TestMethod]
    public async Task Import_WithBatchId_NoBodyAtAll_StillWorks()
    {
        var service = new FakeQuoteImportService();
        using var factory = CreateFactory(TestKey, service);
        var batchId = Guid.NewGuid();

        var response = await CreateClientWithKey(factory)
            .PostAsync($"/api/v1/import?batchId={batchId}", content: null);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(batchId, service.LastAppliedBatchId);
    }

    [TestMethod]
    public async Task Import_WithBatchId_NoKey_Returns401()
    {
        using var factory = CreateFactory(TestKey, new FakeQuoteImportService());
        var response = await factory.CreateClient()
            .PostAsync($"/api/v1/import?batchId={Guid.NewGuid()}", BuildForm(includeFile: false));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Import_WithBatchId_UnknownBatch_Returns404()
    {
        var service = new FakeQuoteImportService { ThrowOnApplyStagedBatch = new ImportBatchNotFoundException(Guid.NewGuid()) };
        using var factory = CreateFactory(TestKey, service);

        var response = await CreateClientWithKey(factory)
            .PostAsync($"/api/v1/import?batchId={Guid.NewGuid()}", BuildForm(includeFile: false));

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Import_WithBatchId_ResultHasPendingConflict_Returns202()
    {
        var batchId = Guid.NewGuid();
        var service = new FakeQuoteImportService
        {
            ReturnResult = new Quotinator.Core.Models.ImportResultResponse
            {
                BatchId        = batchId,
                Preview        = false,
                ConflictPolicy = "review",
                Summary        = new Quotinator.Core.Models.ImportSummary { Total = 1, Imported = 0, Updated = 0, Skipped = 1, Errors = 0 },
                Conflicts =
                [
                    new Quotinator.Core.Models.ImportConflictEntry
                    {
                        QuoteId       = "11111111-1111-1111-1111-111111111111",
                        AppliedPolicy = "review",
                        Status        = "pending",
                        ExistingValue = new Dictionary<string, object?>(),
                        IncomingValue = new Dictionary<string, object?>(),
                    }
                ],
                PendingActionIds = [Guid.NewGuid()],
            }
        };
        using var factory = CreateFactory(TestKey, service);

        var response = await CreateClientWithKey(factory)
            .PostAsync($"/api/v1/import?batchId={batchId}", BuildForm(includeFile: false));

        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
    }

    [TestMethod]
    public async Task Import_ValidSettings_PassesConverterAndPolicyThroughToService()
    {
        var service = new FakeQuoteImportService();
        using var factory = CreateFactory(TestKey, service);
        var settingsJson = """{"converter":"csv","duplicateResolution":{"default":"merge-theirs"}}""";
        var response = await CreateClientWithKey(factory).PostAsync("/api/v1/import", BuildForm(settingsJson: settingsJson));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("csv", service.LastSettings?.Converter);
        Assert.AreEqual(Quotinator.Data.Import.DuplicateResolutionPolicy.MergeTheirs, service.LastSettings?.DuplicateResolution?.Default);
    }
}

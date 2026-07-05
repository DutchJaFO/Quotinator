using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Core.Services;
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
        return form;
    }

    [TestMethod]
    [DataRow("/api/v1/quotes/import")]
    [DataRow("/api/v1/quotes/import/preview")]
    public async Task Import_NoKeyConfigured_Returns401(string path)
    {
        using var factory = CreateFactory(null, new FakeQuoteImportService());
        var response = await factory.CreateClient().PostAsync(path, BuildForm());
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    [DataRow("/api/v1/quotes/import")]
    [DataRow("/api/v1/quotes/import/preview")]
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
        var response = await CreateClientWithKey(factory).PostAsync("/api/v1/quotes/import", BuildForm(includeFile: false));
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task Import_MalformedSettingsJson_Returns422()
    {
        using var factory = CreateFactory(TestKey, new FakeQuoteImportService());
        var response = await CreateClientWithKey(factory)
            .PostAsync("/api/v1/quotes/import", BuildForm(settingsJson: "{ not json"));
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task Import_EnrichTrue_Returns501_BeforeCallingService()
    {
        var service = new FakeQuoteImportService();
        using var factory = CreateFactory(TestKey, service);
        var response = await CreateClientWithKey(factory)
            .PostAsync("/api/v1/quotes/import", BuildForm(settingsJson: """{"enrich":true}"""));

        Assert.AreEqual(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.IsNull(service.LastFileName, "The service must never be called when enrich=true short-circuits first");
    }

    [TestMethod]
    public async Task Import_UnknownConverter_Returns422()
    {
        var service = new FakeQuoteImportService { ThrowOnImport = new UnknownConverterException("bogus") };
        using var factory = CreateFactory(TestKey, service);
        var response = await CreateClientWithKey(factory)
            .PostAsync("/api/v1/quotes/import", BuildForm(settingsJson: """{"converter":"bogus"}"""));

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task Import_ServiceThrowsValidationException_Returns422()
    {
        var service = new FakeQuoteImportService { ThrowOnImport = new QuoteImportValidationException("File contained no quotes.") };
        using var factory = CreateFactory(TestKey, service);
        var response = await CreateClientWithKey(factory).PostAsync("/api/v1/quotes/import", BuildForm());

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task Import_CorrectKeyAndValidFile_Returns200WithResultShape()
    {
        using var factory = CreateFactory(TestKey, new FakeQuoteImportService());
        var response = await CreateClientWithKey(factory).PostAsync("/api/v1/quotes/import", BuildForm());

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(doc.RootElement.TryGetProperty("summary", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("preview", out var preview));
        Assert.IsFalse(preview.GetBoolean());
    }

    [TestMethod]
    public async Task ImportPreview_CorrectKeyAndValidFile_Returns200WithPreviewTrue()
    {
        var service = new FakeQuoteImportService();
        using var factory = CreateFactory(TestKey, service);
        var response = await CreateClientWithKey(factory).PostAsync("/api/v1/quotes/import/preview", BuildForm());

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(true, service.LastPreview);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(doc.RootElement.GetProperty("preview").GetBoolean());
    }

    [TestMethod]
    public async Task Import_ValidSettings_PassesConverterAndPolicyThroughToService()
    {
        var service = new FakeQuoteImportService();
        using var factory = CreateFactory(TestKey, service);
        var settingsJson = """{"converter":"csv","duplicateResolution":{"default":"merge-theirs"}}""";
        var response = await CreateClientWithKey(factory).PostAsync("/api/v1/quotes/import", BuildForm(settingsJson: settingsJson));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("csv", service.LastSettings?.Converter);
        Assert.AreEqual(Quotinator.Data.Import.DuplicateResolutionPolicy.MergeTheirs, service.LastSettings?.DuplicateResolution?.Default);
    }
}

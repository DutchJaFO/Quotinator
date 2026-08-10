using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Helpers;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Api.Tests.Endpoints;

/// <summary>Endpoint tests for <c>/api/v1/import/file-resources</c> (#251).</summary>
[TestClass]
public class ImportFileResourceEndpointsTests
{
    private const string TestKey = "test-admin-key";

    private static WebApplicationFactory<Program> CreateFactory(
        string? adminApiKey = null, IFileResourceRepository? fileResources = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(NoOpDatabaseInitializer.Instance);
                services.AddSingleton<IAuditEntryWriter>(new NoOpAuditEntryWriter());
                services.AddSingleton<IAuditEntryReader>(new NoOpAuditEntryReader());
                services.AddSingleton<ICallerContext>(new NoOpCallerContext());
                services.AddSingleton(fileResources ?? new FakeFileResourceRepository());
            });

            // ConfigureAppConfiguration runs after all file-based sources (including
            // appsettings.local.json), so the in-memory value wins for the test.
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

    private static FileResourceEntity BuildResource(string fileName = "file.json", FileResourceOrigin origin = FileResourceOrigin.System) => new()
    {
        FileName                = fileName,
        Origin                  = new SafeValue<FileResourceOrigin?>(origin.ToString(), origin),
        ContentHash             = "irrelevant-for-this-test",
        LineEnding              = new SafeValue<LineEndingStyle?>(nameof(LineEndingStyle.LF), LineEndingStyle.LF),
        EndsWithTrailingNewline = true,
    };

    // ── GET /import/file-resources — list ──────────────────────────────────────

    [TestMethod]
    public async Task GetFileResources_Returns200WithPageShape()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/import/file-resources", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        Assert.IsTrue(doc.RootElement.TryGetProperty("items",      out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("page",       out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("pageSize",   out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("totalCount", out _));
    }

    [TestMethod]
    public async Task GetFileResources_FilterByOrigin_ReturnsOnlyMatching()
    {
        var fileResources = new FakeFileResourceRepository();
        fileResources.Seed(BuildResource("system.json", FileResourceOrigin.System), ["x"]);
        fileResources.Seed(BuildResource("uploaded.json", FileResourceOrigin.Upload), ["x"]);

        using var factory = CreateFactory(fileResources: fileResources);
        var response = await factory.CreateClient().GetAsync("/api/v1/import/file-resources?origin=upload", TestContext.CancellationToken);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(1, doc.RootElement.GetProperty("totalCount").GetInt32());
    }

    [TestMethod]
    public async Task GetFileResources_InvalidOrigin_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/import/file-resources?origin=bogus", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── Pagination contract (#195's 8-case matrix) ───────────────────────────────

    [TestMethod]
    public async Task GetFileResources_PageZero_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/import/file-resources?page=0", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetFileResources_PageMalformed_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/import/file-resources?page=abc", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetFileResources_PageSizeMalformed_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/import/file-resources?pageSize=abc", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetFileResources_PageSizeNegative_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/import/file-resources?pageSize=-1", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetFileResources_PageSizeAbove500_Returns422NotSilentClamp()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/import/file-resources?pageSize=999", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetFileResources_PageSizeZero_ReturnsAllRowsAsOnePage()
    {
        var fileResources = new FakeFileResourceRepository();
        for (var i = 0; i < 3; i++) fileResources.Seed(BuildResource($"file-{i}.json"), ["x"]);

        using var factory = CreateFactory(fileResources: fileResources);
        var response = await factory.CreateClient().GetAsync("/api/v1/import/file-resources?pageSize=0", TestContext.CancellationToken);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(3, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.AreEqual(3, doc.RootElement.GetProperty("pageSize").GetInt32());
        Assert.AreEqual(3, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    [TestMethod]
    public async Task GetFileResources_PageSizeOmitted_DefaultsTo20()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/import/file-resources", TestContext.CancellationToken);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(20, doc.RootElement.GetProperty("pageSize").GetInt32());
    }

    [TestMethod]
    public async Task GetFileResources_PageBeyondLast_Returns422DistinctDetail()
    {
        var fileResources = new FakeFileResourceRepository();
        fileResources.Seed(BuildResource(), ["x"]);

        using var factory = CreateFactory(fileResources: fileResources);
        var response = await factory.CreateClient().GetAsync("/api/v1/import/file-resources?page=5", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── GET /import/file-resources/{id} — detail ─────────────────────────────────

    [TestMethod]
    public async Task GetFileResourceById_ExistingId_ReturnsFullDetailIncludingLinkedBatchIds()
    {
        var fileResources = new FakeFileResourceRepository();
        var resource = BuildResource();
        var batchId  = Guid.NewGuid();
        fileResources.Seed(resource, ["x"], [batchId]);

        using var factory = CreateFactory(fileResources: fileResources);
        var response = await factory.CreateClient()
            .GetAsync($"/api/v1/import/file-resources/{resource.Id.ToCanonicalId()}", TestContext.CancellationToken);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(1, doc.RootElement.GetProperty("linkedBatchCount").GetInt32());
        Assert.AreEqual(1, doc.RootElement.GetProperty("linkedBatchIds").GetArrayLength());
        Assert.AreEqual(batchId.ToCanonicalId(), doc.RootElement.GetProperty("linkedBatchIds")[0].GetString());
    }

    [TestMethod]
    public async Task GetFileResourceById_UnknownId_Returns404()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient()
            .GetAsync($"/api/v1/import/file-resources/{Guid.NewGuid()}", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetFileResourceById_MalformedId_Returns404NotBadRequest()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient()
            .GetAsync("/api/v1/import/file-resources/not-a-guid", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── GET /import/file-resources/{id}/download ──────────────────────────────

    /// <summary>Reconstructing without a lineEnding override uses the captured file's own recorded fidelity.</summary>
    [TestMethod]
    public async Task DownloadFileResource_ReconstructsOriginalLineEndingByDefault()
    {
        var fileResources = new FakeFileResourceRepository();
        var resource = new FileResourceEntity
        {
            FileName                = "quotinator-curated.json",
            Origin                  = new SafeValue<FileResourceOrigin?>(nameof(FileResourceOrigin.System), FileResourceOrigin.System),
            ContentHash             = "irrelevant-for-this-test",
            LineEnding              = new SafeValue<LineEndingStyle?>(nameof(LineEndingStyle.CRLF), LineEndingStyle.CRLF),
            EndsWithTrailingNewline = true,
        };
        fileResources.Seed(resource, ["line one", "line two"]);

        using var factory = CreateFactory(fileResources: fileResources);
        var response = await factory.CreateClient()
            .GetAsync($"/api/v1/import/file-resources/{resource.Id.ToCanonicalId()}/download", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.CancellationToken);
        Assert.AreEqual("line one\r\nline two\r\n", body);
    }

    /// <summary>A lineEnding query parameter normalizes the reconstructed output to a different style than what was captured.</summary>
    [TestMethod]
    public async Task DownloadFileResource_LineEndingOverride_NormalizesOutput()
    {
        var fileResources = new FakeFileResourceRepository();
        var resource = new FileResourceEntity
        {
            FileName                = "quotinator-curated.json",
            Origin                  = new SafeValue<FileResourceOrigin?>(nameof(FileResourceOrigin.System), FileResourceOrigin.System),
            ContentHash             = "irrelevant-for-this-test",
            LineEnding              = new SafeValue<LineEndingStyle?>(nameof(LineEndingStyle.LF), LineEndingStyle.LF),
            EndsWithTrailingNewline = false,
        };
        fileResources.Seed(resource, ["line one", "line two"]);

        using var factory = CreateFactory(fileResources: fileResources);
        var response = await factory.CreateClient()
            .GetAsync($"/api/v1/import/file-resources/{resource.Id.ToCanonicalId()}/download?lineEnding=crlf", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.CancellationToken);
        Assert.AreEqual("line one\r\nline two", body);
    }

    /// <summary>An unknown id returns 404, not 500 or an empty 200.</summary>
    [TestMethod]
    public async Task DownloadFileResource_UnknownId_Returns404()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient()
            .GetAsync($"/api/v1/import/file-resources/{Guid.NewGuid()}/download", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── POST /import/file-resources/prune ──────────────────────────────────────

    /// <summary>POST /import/file-resources/prune returns 401 when no X-Api-Key header is supplied.</summary>
    [TestMethod]
    public async Task PruneFileResources_NoApiKey_Returns401()
    {
        using var factory = CreateFactory(TestKey);
        var response = await factory.CreateClient()
            .PostAsync("/api/v1/import/file-resources/prune", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A non-numeric keepPerFile returns 422, not the framework binder's bare 400.</summary>
    [TestMethod]
    public async Task PruneFileResources_MalformedKeepPerFile_Returns422()
    {
        using var factory = CreateFactory(TestKey);
        var response = await CreateClientWithKey(factory)
            .PostAsync("/api/v1/import/file-resources/prune?keepPerFile=abc", null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    public TestContext TestContext { get; set; }
}

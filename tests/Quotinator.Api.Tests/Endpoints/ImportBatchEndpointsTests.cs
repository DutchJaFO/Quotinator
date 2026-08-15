using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
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

/// <summary>Endpoint tests for <c>/api/v1/import/batches</c> (#251).</summary>
[TestClass]
public class ImportBatchEndpointsTests
{
    private static WebApplicationFactory<Program> CreateFactory(IImportBatchRepository? batches = null) =>
        new QuotinatorWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(NoOpDatabaseInitializer.Instance);
                services.AddSingleton<IAuditEntryWriter>(new NoOpAuditEntryWriter());
                services.AddSingleton<IAuditEntryReader>(new NoOpAuditEntryReader());
                services.AddSingleton<ICallerContext>(new NoOpCallerContext());
                services.AddSingleton(batches ?? new FakeImportBatchRepository());
            });
        });

    private static ImportBatchEntity BuildBatch(
        string name = "batch.json", ImportBatchType type = ImportBatchType.Seed, ImportBatchStatus status = ImportBatchStatus.Applied) => new()
    {
        Name           = name,
        Type           = new SafeValue<ImportBatchType?>(type.ToString(), type),
        ImportedAt     = SafeDateValue.Now.Raw,
        ConflictPolicy = new SafeValue<DuplicateResolutionPolicy?>(DuplicateResolutionPolicy.NewestWins.ToString(), DuplicateResolutionPolicy.NewestWins),
        Status         = new SafeValue<ImportBatchStatus?>(status.ToString(), status),
    };

    // ── GET /import/batches — list ───────────────────────────────────────────────

    [TestMethod]
    public async Task GetImportBatches_Returns200WithPageShape()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/import/batches", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        Assert.IsTrue(doc.RootElement.TryGetProperty("items",      out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("page",       out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("pageSize",   out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("totalCount", out _));
    }

    [TestMethod]
    public async Task GetImportBatches_FilterByType_ReturnsOnlyMatching()
    {
        var batches = new FakeImportBatchRepository();
        batches.Seed(BuildBatch("seed.json", ImportBatchType.Seed));
        batches.Seed(BuildBatch("import.json", ImportBatchType.Import));

        using var factory = CreateFactory(batches);
        var response = await factory.CreateClient().GetAsync("/api/v1/import/batches?type=import", TestContext.CancellationToken);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(1, doc.RootElement.GetProperty("totalCount").GetInt32());
    }

    [TestMethod]
    public async Task GetImportBatches_FilterByStatus_ReturnsOnlyMatching()
    {
        var batches = new FakeImportBatchRepository();
        batches.Seed(BuildBatch("applied.json", status: ImportBatchStatus.Applied));
        batches.Seed(BuildBatch("staged.json", status: ImportBatchStatus.Staged));

        using var factory = CreateFactory(batches);
        var response = await factory.CreateClient().GetAsync("/api/v1/import/batches?status=staged", TestContext.CancellationToken);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(1, doc.RootElement.GetProperty("totalCount").GetInt32());
    }

    [TestMethod]
    public async Task GetImportBatches_InvalidType_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/import/batches?type=bogus", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetImportBatches_InvalidStatus_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/import/batches?status=bogus", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── Pagination contract (#195's 8-case matrix) ───────────────────────────────

    [TestMethod]
    public async Task GetImportBatches_PageZero_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/import/batches?page=0", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetImportBatches_PageMalformed_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/import/batches?page=abc", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetImportBatches_PageSizeMalformed_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/import/batches?pageSize=abc", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetImportBatches_PageSizeNegative_Returns422()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/import/batches?pageSize=-1", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetImportBatches_PageSizeAbove500_Returns422NotSilentClamp()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/import/batches?pageSize=999", TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task GetImportBatches_PageSizeZero_ReturnsAllRowsAsOnePage()
    {
        var batches = new FakeImportBatchRepository();
        for (var i = 0; i < 3; i++) batches.Seed(BuildBatch($"batch-{i}.json"));

        using var factory = CreateFactory(batches);
        var response = await factory.CreateClient().GetAsync("/api/v1/import/batches?pageSize=0", TestContext.CancellationToken);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(3, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.AreEqual(3, doc.RootElement.GetProperty("pageSize").GetInt32());
        Assert.AreEqual(3, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    [TestMethod]
    public async Task GetImportBatches_PageSizeOmitted_DefaultsTo20()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/import/batches", TestContext.CancellationToken);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(20, doc.RootElement.GetProperty("pageSize").GetInt32());
    }

    [TestMethod]
    public async Task GetImportBatches_PageBeyondLast_Returns422DistinctDetail()
    {
        var batches = new FakeImportBatchRepository();
        batches.Seed(BuildBatch());

        using var factory = CreateFactory(batches);
        var response = await factory.CreateClient().GetAsync("/api/v1/import/batches?page=5", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ── GET /import/batches/{id} — detail ─────────────────────────────────────────

    [TestMethod]
    public async Task GetImportBatchById_ExistingId_ReturnsBatch()
    {
        var batches = new FakeImportBatchRepository();
        var batch = BuildBatch();
        batches.Seed(batch);

        using var factory = CreateFactory(batches);
        var response = await factory.CreateClient()
            .GetAsync($"/api/v1/import/batches/{batch.Id.ToCanonicalId()}", TestContext.CancellationToken);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(batch.Id.ToCanonicalId(), doc.RootElement.GetProperty("id").GetString());
        Assert.AreEqual("batch.json", doc.RootElement.GetProperty("name").GetString());
        Assert.AreEqual("seed", doc.RootElement.GetProperty("type").GetString());
    }

    [TestMethod]
    public async Task GetImportBatchById_UppercaseId_MatchesCaseInsensitively()
    {
        var batches = new FakeImportBatchRepository();
        var batch = BuildBatch();
        batches.Seed(batch);

        using var factory = CreateFactory(batches);
        var response = await factory.CreateClient()
            .GetAsync($"/api/v1/import/batches/{batch.Id.ToCanonicalId().ToUpperInvariant()}", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task GetImportBatchById_UnknownId_Returns404()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient()
            .GetAsync($"/api/v1/import/batches/{Guid.NewGuid()}", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetImportBatchById_MalformedId_Returns404NotBadRequest()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient()
            .GetAsync("/api/v1/import/batches/not-a-guid", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    public TestContext TestContext { get; set; }
}

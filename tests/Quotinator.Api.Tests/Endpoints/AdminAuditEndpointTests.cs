using System.Data;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Api.Tests.Endpoints;

[TestClass]
public class AdminAuditEndpointTests
{
    private const string TestKey = "test-admin-key";

    private static WebApplicationFactory<Program> CreateFactory(
        IAuditReader?  auditReader  = null,
        IAuditWriter?  auditWriter  = null,
        string?        adminApiKey  = TestKey)
    {
        var reader = auditReader ?? new NoOpAuditReader();
        var writer = auditWriter ?? new NoOpAuditWriter();

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(new NoOpDatabaseInitializer());
                services.AddSingleton<IAuditWriter>(writer);
                services.AddSingleton<IAuditReader>(reader);
                services.AddSingleton<ICallerContext>(new NoOpCallerContext());
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

    // ── Response shape ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetAudit_CorrectKey_Returns200WithPageShape()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.GetAsync("/api/v1/admin/audit");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var doc  = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.IsTrue(root.TryGetProperty("totalMatching", out _), "response must have totalMatching");
        Assert.IsTrue(root.TryGetProperty("totalPages",    out _), "response must have totalPages");
        Assert.IsTrue(root.TryGetProperty("page",          out _), "response must have page");
        Assert.IsTrue(root.TryGetProperty("pageSize",      out _), "response must have pageSize");
        Assert.IsTrue(root.TryGetProperty("items",         out _), "response must have items");
    }

    [TestMethod]
    public async Task GetAudit_EmptyResult_ReturnsZeroTotals()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.GetAsync("/api/v1/admin/audit");
        var doc      = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.AreEqual(0, doc.RootElement.GetProperty("totalMatching").GetInt32());
        Assert.AreEqual(0, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    [TestMethod]
    public async Task GetAudit_WithItems_ReturnsItems()
    {
        var entry = new AuditEntry
        {
            Id          = 1,
            TableName   = "Quotes",
            RecordId    = Guid.Empty.ToString("D").ToUpperInvariant(),
            Operation   = AuditOperation.Insert,
            Agent       = "TestRunner/1.0",
            PerformedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
        };

        var stubReader = new StubAuditReader(new AuditPageResult([entry], 1, 50, 1));
        using var factory = CreateFactory(stubReader);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.GetAsync("/api/v1/admin/audit");
        var doc      = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.AreEqual(1, doc.RootElement.GetProperty("totalMatching").GetInt32());
        Assert.AreEqual(1, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    // ── Pagination clamp ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetAudit_PageSizeOver200_ClampedTo200()
    {
        int? capturedPageSize = null;
        var  capturingReader  = new CapturingAuditReader(ps => capturedPageSize = ps);

        using var factory = CreateFactory(capturingReader);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        await client.GetAsync("/api/v1/admin/audit?pageSize=500");

        Assert.AreEqual(200, capturedPageSize, "pageSize above 200 must be clamped to 200");
    }

    // ── GET audit — no auth required ─────────────────────────────────────────

    [TestMethod]
    public async Task GetAudit_NoApiKey_Returns200()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();
        // No X-Api-Key header — GET audit is public.
        var response = await client.GetAsync("/api/v1/admin/audit");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    // ── DELETE audit — auth required ─────────────────────────────────────────

    [TestMethod]
    public async Task DeleteAudit_NoKey_Returns401()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();
        var response      = await client.DeleteAsync("/api/v1/admin/audit");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task DeleteAudit_CorrectKey_Returns204()
    {
        using var factory = CreateFactory();
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);
        var response = await client.DeleteAsync("/api/v1/admin/audit");
        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
    }

    [TestMethod]
    public async Task DeleteAudit_WithTable_PassesTableToClearAsync()
    {
        string? capturedTable = "not-called";
        var capturingWriter   = new CapturingAuditWriter(t => capturedTable = t);

        using var factory = CreateFactory(auditWriter: capturingWriter);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        await client.DeleteAsync("/api/v1/admin/audit?table=Quotes");

        Assert.AreEqual("Quotes", capturedTable, "table query parameter must be forwarded to ClearAsync");
    }

    [TestMethod]
    public async Task DeleteAudit_NoTable_PassesNullToClearAsync()
    {
        string? capturedTable = "not-called";
        var capturingWriter   = new CapturingAuditWriter(t => capturedTable = t);

        using var factory = CreateFactory(auditWriter: capturingWriter);
        using var client  = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        await client.DeleteAsync("/api/v1/admin/audit");

        Assert.IsNull(capturedTable, "null must be forwarded to ClearAsync when no table param is supplied");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class StubAuditReader(AuditPageResult result) : IAuditReader
    {
        public Task<AuditPageResult> GetPagedAsync(string? table, string? recordId, int page, int pageSize)
            => Task.FromResult(result);
    }

    private sealed class CapturingAuditReader(Action<int> onCall) : IAuditReader
    {
        public Task<AuditPageResult> GetPagedAsync(string? table, string? recordId, int page, int pageSize)
        {
            onCall(pageSize);
            return Task.FromResult(new AuditPageResult([], page, pageSize, 0));
        }
    }

    private sealed class CapturingAuditWriter(Action<string?> onClear) : IAuditWriter
    {
        public Task WriteAsync(AuditEntry entry, IDbConnection connection, IDbTransaction? transaction = null)
            => Task.CompletedTask;
        public Task WriteAsync(IReadOnlyList<AuditEntry> entries, IDbConnection connection, IDbTransaction? transaction = null)
            => Task.CompletedTask;
        public Task WriteAsync(AuditEntry entry)
            => Task.CompletedTask;
        public Task ClearAsync(string? table = null)
        {
            onClear(table);
            return Task.CompletedTask;
        }
    }
}

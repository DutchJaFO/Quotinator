using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Data.Testing.NoOps;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Import;
using Quotinator.Data.Repositories;

namespace Quotinator.Api.Tests.Endpoints;

[TestClass]
public class AdminEndpointsTests
{
    private const string TestKey = "test-admin-key";

    private static WebApplicationFactory<Program> CreateFactory(
        string? adminApiKey = null, IDatabaseInitializer? dbInitializer = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton(dbInitializer ?? NoOpDatabaseInitializer.Instance);
                services.AddSingleton<ISystemAuditWriter>(new NoOpSystemAuditWriter());
                services.AddSingleton<ISystemAuditReader>(new NoOpSystemAuditReader());
                services.AddSingleton<ICallerContext>(new NoOpCallerContext());
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

    // ── GET /admin/database/seed/preview ─────────────────────────────────────

    /// <summary>GET /admin/database/seed/preview is publicly accessible — no API key required.</summary>
    [TestMethod]
    public async Task PreviewSeed_NoKey_Returns200()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/admin/database/seed/preview");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>GET /admin/database/seed/preview returns 200 with the expected shape.</summary>
    [TestMethod]
    public async Task PreviewSeed_Returns200WithPreviewShape()
    {
        using var factory = CreateFactory(TestKey);
        var response = await factory.CreateClient().GetAsync("/api/v1/admin/database/seed/preview");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(doc.RootElement.TryGetProperty("files",               out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("totalQuotes",         out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("uniqueQuotes",        out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("crossFileDuplicates", out _));
    }

    // ── POST /admin/database/reseed ───────────────────────────────────────────

    /// <summary>POST /admin/database/reseed returns 401 when AdminApiKey is not configured.</summary>
    [TestMethod]
    public async Task ReseedDatabase_NoKeyConfigured_Returns401()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().PostAsync("/api/v1/admin/database/reseed", null);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>POST /admin/database/reseed returns 401 when the Authorization header is missing.</summary>
    [TestMethod]
    public async Task ReseedDatabase_MissingAuthHeader_Returns401()
    {
        using var factory = CreateFactory(TestKey);
        var response = await factory.CreateClient().PostAsync("/api/v1/admin/database/reseed", null);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>POST /admin/database/reseed returns 401 when the wrong key is supplied.</summary>
    [TestMethod]
    public async Task ReseedDatabase_WrongKey_Returns401()
    {
        using var factory = CreateFactory(TestKey);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", "wrong-key");
        var response = await client.PostAsync("/api/v1/admin/database/reseed", null);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>POST /admin/database/reseed returns 200 with the expected stats shape when the correct key is supplied.</summary>
    [TestMethod]
    public async Task ReseedDatabase_CorrectKey_Returns200WithStatsShape()
    {
        using var factory = CreateFactory(TestKey);
        var response = await CreateClientWithKey(factory).PostAsync("/api/v1/admin/database/reseed", null);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(doc.RootElement.TryGetProperty("quotes",     out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("sources",    out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("characters", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("people",     out _));
    }

    // ── POST /admin/database/reset ────────────────────────────────────────────

    /// <summary>POST /admin/database/reset returns 401 when AdminApiKey is not configured.</summary>
    [TestMethod]
    public async Task ResetDatabase_NoKeyConfigured_Returns401()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().PostAsync("/api/v1/admin/database/reset", null);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>POST /admin/database/reset returns 401 when the Authorization header is missing.</summary>
    [TestMethod]
    public async Task ResetDatabase_MissingAuthHeader_Returns401()
    {
        using var factory = CreateFactory(TestKey);
        var response = await factory.CreateClient().PostAsync("/api/v1/admin/database/reset", null);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>POST /admin/database/reset returns 401 when the wrong key is supplied.</summary>
    [TestMethod]
    public async Task ResetDatabase_WrongKey_Returns401()
    {
        using var factory = CreateFactory(TestKey);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", "wrong-key");
        var response = await client.PostAsync("/api/v1/admin/database/reset", null);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>POST /admin/database/reset returns 200 with the expected stats shape when the correct key is supplied.</summary>
    [TestMethod]
    public async Task ResetDatabase_CorrectKey_Returns200WithStatsShape()
    {
        using var factory = CreateFactory(TestKey);
        var response = await CreateClientWithKey(factory).PostAsync("/api/v1/admin/database/reset", null);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(doc.RootElement.TryGetProperty("quotes",     out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("sources",    out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("characters", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("people",     out _));
    }

    /// <summary>POST /admin/database/reset with no query parameter defaults preserveSchemaVersion to false (#141).</summary>
    [TestMethod]
    public async Task ResetDatabase_NoQueryParam_DefaultsPreserveSchemaVersionFalse()
    {
        var spy = new SpyDatabaseInitializer();
        using var factory = CreateFactory(TestKey, spy);
        var response = await CreateClientWithKey(factory).PostAsync("/api/v1/admin/database/reset", null);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(false, spy.LastPreserveSchemaVersion);
    }

    /// <summary>POST /admin/database/reset?preserveSchemaVersion=true threads the flag through to ResetAsync (#141).</summary>
    [TestMethod]
    public async Task ResetDatabase_PreserveSchemaVersionTrue_Returns200AndPassesFlagThrough()
    {
        var spy = new SpyDatabaseInitializer();
        using var factory = CreateFactory(TestKey, spy);
        var response = await CreateClientWithKey(factory)
            .PostAsync("/api/v1/admin/database/reset?preserveSchemaVersion=true", null);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(true, spy.LastPreserveSchemaVersion);
    }

    private sealed class SpyDatabaseInitializer : IDatabaseInitializer
    {
        public bool? LastPreserveSchemaVersion { get; private set; }

        public int    SchemaVersion    => 5;
        public int    DataSchemaVersion => 2;
        public int    QuoteCount       => 0;
        public int    SourceCount      => 0;
        public int    CharacterCount   => 0;
        public int    PeopleCount      => 0;
        public string? MigrationApplied => null;
        public IReadOnlyList<SeedDuplicateRecord> LastSeedDuplicates => [];

        public Task InitialiseAsync() => Task.CompletedTask;
        public Task ReseedAsync()     => Task.CompletedTask;

        public Task ResetAsync(bool preserveSchemaVersion = false)
        {
            LastPreserveSchemaVersion = preserveSchemaVersion;
            return Task.CompletedTask;
        }

        public Task<SeedPreviewResult> PreviewSeedAsync()
            => Task.FromResult(new SeedPreviewResult([], [], 0, 0));
    }
}

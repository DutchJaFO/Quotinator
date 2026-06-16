using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Core.Data;
using Quotinator.Core.Services;

namespace Quotinator.Api.Tests.Endpoints;

[TestClass]
public class AdminEndpointsTests
{
    private const string TestKey = "test-admin-key";

    private static WebApplicationFactory<Program> CreateFactory(string? adminApiKey = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(new NoOpDatabaseInitializer());
            });

            if (adminApiKey is not null)
            {
                builder.UseSetting("Quotinator:AdminApiKey", adminApiKey);
            }
        });

    private static HttpClient CreateClientWithKey(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestKey);
        return client;
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
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-key");
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
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-key");
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
}

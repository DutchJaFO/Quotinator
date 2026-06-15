using System.Net;
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
    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(new NoOpDatabaseInitializer());
            }));

    // ── POST /admin/database/reseed ───────────────────────────────────────────

    /// <summary>POST /admin/database/reseed returns 200 with the expected stats shape.</summary>
    [TestMethod]
    public async Task ReseedDatabase_Returns200WithStatsShape()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().PostAsync("/api/v1/admin/database/reseed", null);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(doc.RootElement.TryGetProperty("quotes",     out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("sources",    out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("characters", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("people",     out _));
    }

    // ── POST /admin/database/reset ────────────────────────────────────────────

    /// <summary>POST /admin/database/reset returns 200 with the expected stats shape.</summary>
    [TestMethod]
    public async Task ResetDatabase_Returns200WithStatsShape()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().PostAsync("/api/v1/admin/database/reset", null);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(doc.RootElement.TryGetProperty("quotes",     out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("sources",    out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("characters", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("people",     out _));
    }

}

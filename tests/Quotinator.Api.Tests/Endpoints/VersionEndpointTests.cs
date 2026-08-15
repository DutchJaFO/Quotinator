using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Core.Repositories;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Api.Tests.Endpoints;

[TestClass]
public class VersionEndpointTests
{
    private static WebApplicationFactory<Program> CreateFactory() =>
        new QuotinatorWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(new NoOpDatabaseInitializer());
                services.AddSingleton<ISeriesNameResolver>(new FakeSeriesNameResolver(
                    new Dictionary<string, Guid> { [FakeQuoteService.MiddleEarthSeries.Name] = Guid.Parse(FakeQuoteService.MiddleEarthSeries.Id) }));
                services.AddSingleton<IUniverseNameResolver>(new FakeUniverseNameResolver(
                    new Dictionary<string, Guid> { [FakeQuoteService.MiddleEarthUniverse.Name] = Guid.Parse(FakeQuoteService.MiddleEarthUniverse.Id) }));
            }));

    /// <summary>The database stats object reports every entity-type count <see cref="IDatabaseInitializer"/> exposes, not just the original four (issue #221's SeriesCount/UniverseCount/StageDirectionCount/SoundCueCount/ConversationCount had never been added here).</summary>
    [TestMethod]
    public async Task GetVersion_DatabaseStats_IncludesEveryEntityTypeCount()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/version", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var database = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken))
            .RootElement.GetProperty("database");

        foreach (var field in new[]
        {
            "schemaVersion", "quotes", "sources", "characters", "people",
            "series", "universes", "stageDirections", "soundCues", "conversations",
        })
            Assert.IsTrue(database.TryGetProperty(field, out _), $"database.{field} missing from /version response");
    }

    public TestContext TestContext { get; set; }
}

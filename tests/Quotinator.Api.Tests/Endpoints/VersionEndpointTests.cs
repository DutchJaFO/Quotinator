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

    /// <summary>
    /// The JSON field each <see cref="IDatabaseInitializer"/> count property is published as by
    /// <c>/version</c>. The pluralisation is irregular (<c>PeopleCount</c> → <c>people</c>,
    /// <c>SeriesCount</c> → <c>series</c>, <c>UniverseCount</c> → <c>universes</c>), so no mechanical
    /// transform can derive it — but <see cref="GetVersion_DatabaseStats_MapCoversEveryCountProperty"/>
    /// asserts this map covers every property, so a new entity cannot be added without landing here.
    /// </summary>
    private static readonly Dictionary<string, string> CountPropertyToJsonField = new()
    {
        [nameof(IDatabaseInitializer.QuoteCount)]          = "quotes",
        [nameof(IDatabaseInitializer.SourceCount)]         = "sources",
        [nameof(IDatabaseInitializer.CharacterCount)]      = "characters",
        [nameof(IDatabaseInitializer.PeopleCount)]         = "people",
        [nameof(IDatabaseInitializer.SeriesCount)]         = "series",
        [nameof(IDatabaseInitializer.SeasonCount)]         = "seasons",
        [nameof(IDatabaseInitializer.UniverseCount)]       = "universes",
        [nameof(IDatabaseInitializer.StageDirectionCount)] = "stageDirections",
        [nameof(IDatabaseInitializer.SoundCueCount)]       = "soundCues",
        [nameof(IDatabaseInitializer.ConversationCount)]   = "conversations",
    };

    /// <summary>
    /// Every <c>*Count</c> property on <see cref="IDatabaseInitializer"/> has an entry in
    /// <see cref="CountPropertyToJsonField"/>. Without this, the map below degrades into the
    /// hand-maintained list it replaced — which is exactly how #221's five counts, and then #375's
    /// Season, each reached production unpublished.
    /// </summary>
    [TestMethod]
    public void GetVersion_DatabaseStats_MapCoversEveryCountProperty()
    {
        string[] countProperties = [.. typeof(IDatabaseInitializer).GetProperties()
            .Select(p => p.Name)
            .Where(n => n.EndsWith("Count", StringComparison.Ordinal))];

        string[] unmapped = [.. countProperties.Except(CountPropertyToJsonField.Keys).Order()];
        string[] stale    = [.. CountPropertyToJsonField.Keys.Except(countProperties).Order()];

        Assert.IsEmpty(unmapped, $"IDatabaseInitializer count properties with no /version field mapped: {string.Join(", ", unmapped)}");
        Assert.IsEmpty(stale, $"/version field map names properties IDatabaseInitializer no longer has: {string.Join(", ", stale)}");
    }

    /// <summary>The database stats object reports every entity-type count <see cref="IDatabaseInitializer"/> exposes, not just the original four (issue #221's SeriesCount/UniverseCount/StageDirectionCount/SoundCueCount/ConversationCount had never been added here, and #375's SeasonCount repeated it).</summary>
    [TestMethod]
    public async Task GetVersion_DatabaseStats_IncludesEveryEntityTypeCount()
    {
        using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/version", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var database = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken))
            .RootElement.GetProperty("database");

        Assert.IsTrue(database.TryGetProperty("schemaVersion", out _), "database.schemaVersion missing from /version response");
        foreach (string field in CountPropertyToJsonField.Values)
            Assert.IsTrue(database.TryGetProperty(field, out _), $"database.{field} missing from /version response");
    }

    public TestContext TestContext { get; set; }
}

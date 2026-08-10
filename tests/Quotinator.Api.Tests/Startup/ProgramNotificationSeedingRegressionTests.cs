using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// Regression guard for a real bug found live while implementing #279's Step 9: the app's
/// #279-operation-id-rename notification is seeded via the *real* <c>INotificationReader</c>/
/// <c>INotificationWriter</c> (not overridden here, deliberately — most endpoint test files don't
/// override them either), while <see cref="NoOpDatabaseInitializer"/> never creates
/// <c>System_Notification</c>. The first wiring shared its <c>try</c>/<c>catch</c> with
/// <c>Program.cs</c>'s critical DB-init block, so the resulting "no such table" exception marked the
/// whole app unhealthy — 336 of 663 <c>Quotinator.Api.Tests</c> immediately failed. This test proves
/// the fix directly: a failure to seed the announcement notification must never affect
/// <see cref="Quotinator.Api.Startup.DatabaseHealthState"/>.
/// </summary>
[TestClass]
public class ProgramNotificationSeedingRegressionTests
{
    [TestMethod]
    public async Task Health_NoOpDatabaseInitializer_StaysHealthyDespiteMissingNotificationTable()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(NoOpDatabaseInitializer.Instance);
                // INotificationReader/INotificationWriter deliberately NOT overridden — this test
                // exists specifically to prove the real implementations' failure against a
                // non-existent System_Notification table doesn't propagate.
            }));

        var response = await factory.CreateClient().GetAsync("/api/v1/health", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("{\"status\":\"healthy\"}", await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
    }

    /// <summary>
    /// #289: the second concrete producer for #278's notification mechanism — proves the actual
    /// Program.cs wiring, not just <see cref="Quotinator.Api.Startup.NotificationSeeding.SeedOnceAsync"/>
    /// in isolation (already covered by <see cref="NotificationSeedingTests"/>). A stub
    /// <see cref="IDatabaseInitializer"/> reports a schema-version overshoot; a real startup must seed
    /// exactly one ActionRequired notification mentioning both recorded versions.
    /// </summary>
    [TestMethod]
    public async Task Startup_SchemaVersionOvershootDetected_SeedsActionRequiredNotification()
    {
        var writer = new FakeNotificationWriter();

        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(new OvershootDatabaseInitializer());
                services.AddSingleton<Quotinator.Data.Repositories.INotificationWriter>(writer);
                services.AddSingleton<Quotinator.Data.Repositories.INotificationReader>(new FakeNotificationReader());
            }));

        using var client = factory.CreateClient();
        await client.GetAsync("/api/v1/health", TestContext.CancellationToken);

        // #279's own unconditional operation-id-rename notification also seeds whenever dbHealth is
        // healthy, alongside this one — assert on the specific #289 message, not the total count.
        var overshootMessage = writer.WrittenMessages.SingleOrDefault(m => m.Contains("data v3") && m.Contains("app v5"));
        Assert.IsNotNull(overshootMessage, "the schema-version-overshoot notification must have been seeded");
    }

    private sealed class OvershootDatabaseInitializer : IDatabaseInitializer
    {
        public int SchemaVersion => 5;
        public int DataSchemaVersion => 3;
        public int QuoteCount => 0;
        public int SourceCount => 0;
        public int CharacterCount => 0;
        public int PeopleCount => 0;
        public int SeriesCount => 0;
        public int UniverseCount => 0;
        public int StageDirectionCount => 0;
        public int SoundCueCount => 0;
        public int ConversationCount => 0;
        public string? MigrationApplied => null;
        public bool SchemaVersionOvershootDetected => true;
        public IReadOnlyList<FileImportReport> LastSeedReport => [];
        public Task InitialiseAsync() => Task.CompletedTask;
        public Task ReseedAsync(bool forceSourceRefresh = false) => Task.CompletedTask;
        public Task ResetAsync(bool preserveSchemaVersion = false, bool forceSourceRefresh = false) => Task.CompletedTask;
        public Task<SeedPreviewResult> PreviewSeedAsync() => Task.FromResult(new SeedPreviewResult([], []));
        public Task<SourceCacheResolution> RefreshSourcesAsync(bool force = false) => Task.FromResult(new SourceCacheResolution([], []));
    }

    public TestContext TestContext { get; set; }
}

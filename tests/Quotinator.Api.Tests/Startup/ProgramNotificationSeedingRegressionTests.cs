using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
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

    public TestContext TestContext { get; set; }
}

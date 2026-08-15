using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Startup;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// Proves #313's guard: a client from <see cref="QuotinatorWebApplicationFactory"/> never observes the
/// startup wait page, and a startup that never completes fails loudly instead of hanging the suite.
/// </summary>
[TestClass]
public class StartupReadinessTests
{
    /// <summary>
    /// The direct statement of the guarantee: by the time the factory hands back a client, the app has
    /// already called <c>MarkComplete</c>, so <c>StartupWaitMiddleware</c> can no longer intercept.
    /// Asserting the flag rather than a response code is deliberate — a passing status code proves only
    /// that this run happened to win the race, whereas the flag is the condition itself.
    /// </summary>
    [TestMethod]
    public void CreateClient_ReturnsOnlyAfterStartupIsComplete()
    {
        using var factory = new QuotinatorWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(NoOpDatabaseInitializer.Instance);
            }));

        using var client = factory.CreateClient();

        var phase = factory.Services.GetRequiredService<StartupPhaseState>();
        Assert.IsTrue(phase.IsComplete,
            "The factory must not hand back a client before startup completes — otherwise every request it makes " +
            "can be served the startup wait page instead of reaching its endpoint (#313).");
    }

    /// <summary>
    /// The failure this guard exists to prevent, stated as an assertion: an endpoint request must reach
    /// its endpoint, not the wait page. `/api/v1/conversations?page=0` is the exact request that failed
    /// intermittently and led to #313 — it is non-exempt, so the wait page would answer it `200 OK`
    /// while the endpoint itself answers `422`.
    /// </summary>
    [TestMethod]
    public async Task EndpointRequest_ReachesEndpointRatherThanWaitPage()
    {
        using var factory = new QuotinatorWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(NoOpDatabaseInitializer.Instance);
            }));

        var response = await factory.CreateClient().GetAsync("/api/v1/conversations?page=0", TestContext.CancellationToken);
        var body     = await response.Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.DoesNotContain("Quotinator is starting up", body,
            "A non-exempt request must never be answered by the startup wait page once the factory has returned a client.");
    }

    /// <summary>
    /// A startup that never completes must fail with a clear, bounded error. Without this, the guard
    /// would convert an intermittent wrong-answer into an indefinite hang — a worse failure mode, and
    /// one no CI timeout explains usefully.
    /// </summary>
    [TestMethod]
    public void WaitUntilComplete_NeverCompletes_ThrowsClearTimeoutRatherThanHanging()
    {
        var ex = Assert.ThrowsExactly<TimeoutException>(() =>
            StartupReadiness.WaitUntilComplete(
                () => false,
                timeout: TimeSpan.FromMilliseconds(50),
                pollInterval: TimeSpan.FromMilliseconds(5)));

        Assert.Contains("MarkComplete", ex.Message, "The message must name what the app failed to do, not just report a timeout.");
        Assert.Contains("#313", ex.Message, "The message must point at the issue explaining why this guard exists.");
    }

    /// <summary>An already-complete startup returns immediately rather than paying the poll interval.</summary>
    [TestMethod]
    public void WaitUntilComplete_AlreadyComplete_ReturnsWithoutWaiting()
    {
        StartupReadiness.WaitUntilComplete(() => true, timeout: TimeSpan.FromMilliseconds(50));
    }

    public TestContext TestContext { get; set; }
}

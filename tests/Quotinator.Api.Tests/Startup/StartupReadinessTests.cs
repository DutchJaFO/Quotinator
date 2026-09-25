using Microsoft.AspNetCore.Mvc.Testing;
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
    /// Asserting the flag rather than a response code is deliberate: a passing status code proves only
    /// that this run happened to win the race, whereas the flag is the condition itself.
    /// </summary>
    [TestMethod]
    public void CreateClient_ReturnsOnlyAfterStartupIsComplete()
    {
        using WebApplicationFactory<Program> factory = new QuotinatorWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IQuoteService>(new FakeQuoteService());
                services.AddSingleton<IDatabaseInitializer>(NoOpDatabaseInitializer.Instance);
            }));

        using HttpClient client = factory.CreateClient();

        StartupPhaseState phase = factory.Services.GetRequiredService<StartupPhaseState>();
        Assert.IsTrue(phase.IsComplete,
            "The factory must not hand back a client before startup completes: otherwise every request it makes " +
            "can be served the startup wait page instead of reaching its endpoint (#313).");
    }

    /// <summary>
    /// A startup that never completes must fail with a clear, bounded error. Without this, the guard
    /// would convert an intermittent wrong-answer into an indefinite hang: a worse failure mode, and
    /// one no CI timeout explains usefully.
    /// </summary>
    [TestMethod]
    public void WaitUntilComplete_NeverCompletes_ThrowsClearTimeoutRatherThanHanging()
    {
        TimeoutException ex = Assert.ThrowsExactly<TimeoutException>(() =>
            StartupReadiness.WaitUntilComplete(
                () => false,
                timeout: TimeSpan.FromMilliseconds(50),
                pollInterval: TimeSpan.FromMilliseconds(5)));

        Assert.Contains("MarkComplete", ex.Message, "The message must name what the app failed to do, not just report a timeout.");
        Assert.Contains("#313", ex.Message, "The message must point at the issue explaining why this guard exists.");
    }

    /// <summary>
    /// The positive counterpart of the timeout: an already-complete startup returns at once. Asked exactly
    /// once means no poll interval was paid; a loop would have asked again after sleeping.
    /// </summary>
    [TestMethod]
    public void WaitUntilComplete_AlreadyComplete_ReturnsWithoutWaiting()
    {
        int asked = 0;

        StartupReadiness.WaitUntilComplete(() => { asked++; return true; }, timeout: TimeSpan.FromMilliseconds(50));

        Assert.AreEqual(1, asked, "A startup already complete must be recognised on the first check, with no poll.");
    }
}

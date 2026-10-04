using System.Globalization;
using Microsoft.AspNetCore.Http;
using Quotinator.Api.Middleware;
using Quotinator.Api.Startup;
using Quotinator.Core.Services;
using Quotinator.Data.Testing.Database;

namespace Quotinator.Api.Tests.Middleware;

[TestClass]
public class StartupWaitMiddlewareTests
{
    private TempDirectory _i18nDir = null!;
    private CultureInfo _savedCulture = CultureInfo.CurrentUICulture;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Setup()
    {
        _savedCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("en-GB");

        _i18nDir = new TempDirectory("quotinator_startupwait_test_");
        File.WriteAllText(Path.Combine(_i18nDir.Path, "UI.en-GB.json"),
            """{"StartupWaitHeading": "Quotinator is starting up", "StartupWaitBody": "Please wait."}""");
    }

    [TestCleanup]
    public void Cleanup()
    {
        CultureInfo.CurrentUICulture = _savedCulture;
        _i18nDir.Dispose();
    }

    // -------------------------------------------------------------------------
    #region Helpers

    private static DefaultHttpContext MakeContext(string path, string method = "GET", string? accept = null)
    {
        DefaultHttpContext ctx = new();
        ctx.Request.Path = new PathString(path);
        ctx.Request.Method = method;
        if (accept is not null)
            ctx.Request.Headers.Accept = accept;
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static (RequestDelegate Next, Func<bool> WasCalled) SpyNext()
    {
        bool called = false;
        Task next(HttpContext ctx)
        {
            called = true;
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        }
        return (next, () => called);
    }

    private StartupWaitMiddleware CreateMiddleware(bool isComplete)
    {
        StartupPhaseState phase = new();
        if (isComplete) phase.MarkComplete();
        return new StartupWaitMiddleware(phase, new ApiLocalizer(_i18nDir.Path));
    }

    private async Task<string> BodyOf(DefaultHttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        return await new StreamReader(context.Response.Body).ReadToEndAsync(TestContext.CancellationToken);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region The gate holds, and lets go

    /// <summary>A request during startup must not reach the handler behind the gate.</summary>
    [TestMethod]
    public async Task DuringStartup_TheRequestNeverReachesTheHandler()
    {
        (RequestDelegate? next, Func<bool>? wasCalled) = SpyNext();

        await CreateMiddleware(isComplete: false).InvokeAsync(MakeContext("/api/v1/quotes/random"), next);

        Assert.IsFalse(wasCalled());
    }

    /// <summary>
    /// A write during startup is refused, never reported as succeeding (#419). The status is the whole
    /// point: before this, every gated request was answered <c>200</c>, so a caller that posted a reset
    /// was told it had worked while nothing had happened.
    /// </summary>
    [TestMethod]
    public async Task NonGetDuringStartup_Answers503()
    {
        DefaultHttpContext context = MakeContext("/api/v1/admin/database/reset", method: "POST");

        await CreateMiddleware(isComplete: false).InvokeAsync(context, SpyNext().Next);

        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
    }

    /// <summary>
    /// The same write once startup is complete reaches its handler. Pairs with
    /// <see cref="NonGetDuringStartup_Answers503"/>: without this, that test passes against a middleware
    /// that refuses writes for ever.
    /// </summary>
    [TestMethod]
    public async Task NonGetAfterStartup_ReachesTheHandler()
    {
        (RequestDelegate? next, Func<bool>? wasCalled) = SpyNext();

        await CreateMiddleware(isComplete: true)
            .InvokeAsync(MakeContext("/api/v1/admin/database/reset", method: "POST"), next);

        Assert.IsTrue(wasCalled());
    }

    /// <summary>
    /// A GET is refused the same way (developer, 2026-10-03): a <c>200</c> carrying content the caller
    /// did not ask for breaks its expectations whatever the method, so the method is not what decides
    /// the status.
    /// </summary>
    [TestMethod]
    public async Task GetDuringStartup_Answers503()
    {
        DefaultHttpContext context = MakeContext("/api/v1/quotes/random");

        await CreateMiddleware(isComplete: false).InvokeAsync(context, SpyNext().Next);

        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
    }

    /// <summary>Pairs with <see cref="GetDuringStartup_Answers503"/>, for the same reason.</summary>
    [TestMethod]
    public async Task GetAfterStartup_ReachesTheHandler()
    {
        (RequestDelegate? next, Func<bool>? wasCalled) = SpyNext();

        await CreateMiddleware(isComplete: true).InvokeAsync(MakeContext("/api/v1/quotes/random"), next);

        Assert.IsTrue(wasCalled());
    }

    /// <summary>A refusal says when to come back, rather than leaving the caller to guess.</summary>
    [TestMethod]
    public async Task DuringStartup_TheRefusalCarriesRetryAfter()
    {
        DefaultHttpContext context = MakeContext("/api/v1/quotes/random");

        await CreateMiddleware(isComplete: false).InvokeAsync(context, SpyNext().Next);

        Assert.IsTrue(context.Response.Headers.ContainsKey("Retry-After"));
    }

    #endregion

    // -------------------------------------------------------------------------
    #region What each caller is given

    /// <summary>An API caller gets a machine-readable refusal, not a web page.</summary>
    [TestMethod]
    public async Task BodyIsProblemDetails_WhenJsonIsAccepted()
    {
        DefaultHttpContext context = MakeContext("/api/v1/quotes/random", accept: "application/json");

        await CreateMiddleware(isComplete: false).InvokeAsync(context, SpyNext().Next);

        Assert.StartsWith("application/problem+json", context.Response.ContentType!);
    }

    /// <summary>
    /// A browser still gets the self-contained, auto-refreshing page. Pairs with
    /// <see cref="BodyIsProblemDetails_WhenJsonIsAccepted"/>: one <c>Accept</c> header must not serve
    /// both, which is what either test alone would allow.
    /// </summary>
    [TestMethod]
    public async Task BodyIsTheWaitPage_WhenHtmlIsAccepted()
    {
        DefaultHttpContext context = MakeContext("/", accept: "text/html");

        await CreateMiddleware(isComplete: false).InvokeAsync(context, SpyNext().Next);

        Assert.StartsWith("text/html", context.Response.ContentType!);
    }

    /// <summary>The page keeps the text it had: refusing with a status did not cost the explanation.</summary>
    [TestMethod]
    public async Task TheWaitPage_StillExplainsItself()
    {
        DefaultHttpContext context = MakeContext("/", accept: "text/html");

        await CreateMiddleware(isComplete: false).InvokeAsync(context, SpyNext().Next);

        Assert.Contains("Quotinator is starting up", await BodyOf(context));
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Which paths answer anyway

    /// <summary>
    /// <c>/api/v1/health</c> is the readiness contract (developer, 2026-10-03): it is what an external
    /// caller polls to learn whether the rest of the API can be used, so it is the one path the gate
    /// never holds. Also the control for the refusal tests above: without it they would pass against a
    /// middleware that gated every path including this one, leaving nothing able to report readiness.
    /// </summary>
    [TestMethod]
    public async Task HealthIsNotGated()
    {
        (RequestDelegate? next, Func<bool>? wasCalled) = SpyNext();

        await CreateMiddleware(isComplete: false).InvokeAsync(MakeContext("/api/v1/health"), next);

        Assert.IsTrue(wasCalled());
    }

    /// <summary>
    /// <c>/api/v1/version</c> is gated (developer, 2026-10-03). It cannot report complete version
    /// information or a ready state until startup has finished, and a caller could otherwise reach for
    /// it as a status endpoint instead of <c>/health</c>. Its ready-state answer is unchanged, which
    /// <c>VersionEndpointTests</c> holds: that is this test's pair, and without it this one passes
    /// against an endpoint that was broken rather than gated.
    /// </summary>
    [TestMethod]
    public async Task VersionIsGatedDuringStartup()
    {
        DefaultHttpContext context = MakeContext("/api/v1/version");

        await CreateMiddleware(isComplete: false).InvokeAsync(context, SpyNext().Next);

        Assert.AreNotEqual(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    #endregion
}

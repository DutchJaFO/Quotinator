using System.Globalization;
using Microsoft.AspNetCore.Http;
using Quotinator.Api.Middleware;
using Quotinator.Api.Startup;
using Quotinator.Core.Services;

namespace Quotinator.Api.Tests.Middleware;

[TestClass]
public class StartupWaitMiddlewareTests
{
    private string _i18nDir = string.Empty;
    private CultureInfo _savedCulture = CultureInfo.CurrentUICulture;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Setup()
    {
        _savedCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("en-GB");

        _i18nDir = Path.Combine(Path.GetTempPath(), $"quotinator-startupwait-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_i18nDir);
        File.WriteAllText(Path.Combine(_i18nDir, "UI.en-GB.json"),
            """{"StartupWaitHeading": "Quotinator is starting up", "StartupWaitBody": "Please wait."}""");
    }

    [TestCleanup]
    public void Cleanup()
    {
        CultureInfo.CurrentUICulture = _savedCulture;
        Directory.Delete(_i18nDir, recursive: true);
    }

    // -------------------------------------------------------------------------
    #region Helpers

    private static DefaultHttpContext MakeContext(string path)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = new PathString(path);
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static (RequestDelegate Next, Func<bool> WasCalled) SpyNext()
    {
        var called = false;
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
        var phase = new StartupPhaseState();
        if (isComplete) phase.MarkComplete();
        return new StartupWaitMiddleware(phase, new ApiLocalizer(_i18nDir));
    }

    #endregion

    [TestMethod]
    public async Task Invoke_InitialisationInProgress_ServesWaitPage()
    {
        var middleware = CreateMiddleware(isComplete: false);
        var (next, wasCalled) = SpyNext();
        var context = MakeContext("/api/v1/quotes/random");

        await middleware.InvokeAsync(context, next);

        Assert.IsFalse(wasCalled(), "A request during initialisation must never reach the real handler");
        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.StartsWith("text/html", context.Response.ContentType!);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync(TestContext.CancellationToken);
        Assert.Contains("Quotinator is starting up", body);
        Assert.Contains("Please wait.", body);
    }

    [TestMethod]
    public async Task Invoke_InitialisationComplete_PassesThroughToNextMiddleware()
    {
        var middleware = CreateMiddleware(isComplete: true);
        var (next, wasCalled) = SpyNext();

        await middleware.InvokeAsync(MakeContext("/api/v1/quotes/random"), next);

        Assert.IsTrue(wasCalled(), "Once initialisation is complete, every request must reach the real handler");
    }

    [DataRow("/api/v1/health")]
    [DataRow("/api/v1/version")]
    [TestMethod]
    public async Task Invoke_HealthEndpoint_ExemptFromWaitGate(string path)
    {
        var middleware = CreateMiddleware(isComplete: false);
        var (next, wasCalled) = SpyNext();

        await middleware.InvokeAsync(MakeContext(path), next);

        Assert.IsTrue(wasCalled(), $"{path} must stay reachable even while initialisation is still in progress");
    }
}

using Microsoft.AspNetCore.Http;
using Quotinator.Api.Middleware;
using Quotinator.Api.Startup;

namespace Quotinator.Api.Tests.Middleware;

[TestClass]
public class DatabaseHealthGateMiddlewareTests
{
    public TestContext TestContext { get; set; } = null!;

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

    #endregion

    [TestMethod]
    public async Task Healthy_AnyPath_CallsNext()
    {
        var middleware = new DatabaseHealthGateMiddleware(new DatabaseHealthState());
        var (next, wasCalled) = SpyNext();

        await middleware.InvokeAsync(MakeContext("/api/v1/quotes/random"), next);

        Assert.IsTrue(wasCalled(), "A healthy state must never intercept any request");
    }

    [TestMethod]
    public async Task Unhealthy_NonExemptPath_Returns503AndSkipsNext()
    {
        var health = new DatabaseHealthState();
        health.MarkFailed("schema mismatch");
        var middleware = new DatabaseHealthGateMiddleware(health);
        var (next, wasCalled) = SpyNext();
        var context = MakeContext("/api/v1/quotes/random");

        await middleware.InvokeAsync(context, next);

        Assert.IsFalse(wasCalled(), "A degraded request must never reach the real handler");
        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
    }

    [TestMethod]
    public async Task Unhealthy_NonExemptPath_ResponseBodyIncludesFailureReason()
    {
        var health = new DatabaseHealthState();
        health.MarkFailed("schema mismatch");
        var middleware = new DatabaseHealthGateMiddleware(health);
        var context = MakeContext("/api/v1/quotes/random");

        await middleware.InvokeAsync(context, SpyNext().Next);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync(TestContext.CancellationToken);
        Assert.Contains("schema mismatch", body);
        Assert.Contains("\"status\":\"unavailable\"", body);
    }

    [DataRow("/api/v1/health")]
    [DataRow("/api/v1/version")]
    [DataRow("/api/v1/admin/database/reset")]
    [DataRow("/openapi/v1.json")]
    [DataRow("/scalar/v1")]
    [DataRow("/_framework/blazor.web.js")]
    [DataRow("/")]
    [DataRow("/rest-api")]
    [DataRow("/about")]
    [DataRow("/stats")]
    [DataRow("/_blazor")]
    [DataRow("/app.khy4lop6wu.css")]
    [DataRow("/Quotinator.Api.ngd3z69k33.styles.css")]
    [DataRow("/favicon.png")]
    [TestMethod]
    public async Task Unhealthy_ExemptPath_CallsNext(string path)
    {
        var health = new DatabaseHealthState();
        health.MarkFailed("schema mismatch");
        var middleware = new DatabaseHealthGateMiddleware(health);
        var (next, wasCalled) = SpyNext();

        await middleware.InvokeAsync(MakeContext(path), next);

        Assert.IsTrue(wasCalled(), $"{path} must stay reachable even when the database is unhealthy");
    }

    [TestMethod]
    public async Task Unhealthy_QuotesEndpoint_StaysGated()
    {
        var health = new DatabaseHealthState();
        health.MarkFailed("schema mismatch");
        var middleware = new DatabaseHealthGateMiddleware(health);
        var (next, wasCalled) = SpyNext();
        var context = MakeContext("/api/v1/quotes/random");

        await middleware.InvokeAsync(context, next);

        Assert.IsFalse(wasCalled(), "#263's new Blazor-route exemptions must not widen the gate for real REST data endpoints");
        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
    }
}

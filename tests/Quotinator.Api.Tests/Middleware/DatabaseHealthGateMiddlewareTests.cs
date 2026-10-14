using Microsoft.AspNetCore.Http;
using Quotinator.Api.Enums;
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
        DefaultHttpContext ctx = new();
        ctx.Request.Path = new PathString(path);
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

    #endregion

    [TestMethod]
    public async Task Healthy_AnyPath_CallsNext()
    {
        DatabaseHealthGateMiddleware middleware = new(new DatabaseHealthState());
        (RequestDelegate? next, Func<bool>? wasCalled) = SpyNext();

        await middleware.InvokeAsync(MakeContext("/api/v1/quotes/random"), next);

        Assert.IsTrue(wasCalled(), "A healthy state must never intercept any request");
    }

    [TestMethod]
    public async Task Unhealthy_NonExemptPath_Returns503AndSkipsNext()
    {
        DatabaseHealthState health = new();
        health.MarkFailed("schema mismatch", DatabaseFaultKind.ExpectedSchemaMissing);
        DatabaseHealthGateMiddleware middleware = new(health);
        (RequestDelegate? next, Func<bool>? wasCalled) = SpyNext();
        DefaultHttpContext context = MakeContext("/api/v1/quotes/random");

        await middleware.InvokeAsync(context, next);

        Assert.IsFalse(wasCalled(), "A degraded request must never reach the real handler");
        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
    }

    [TestMethod]
    public async Task Unhealthy_NonExemptPath_ResponseBodyIncludesFailureReason()
    {
        DatabaseHealthState health = new();
        health.MarkFailed("schema mismatch", DatabaseFaultKind.ExpectedSchemaMissing);
        DatabaseHealthGateMiddleware middleware = new(health);
        DefaultHttpContext context = MakeContext("/api/v1/quotes/random");

        await middleware.InvokeAsync(context, SpyNext().Next);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        string body = await new StreamReader(context.Response.Body).ReadToEndAsync(TestContext.CancellationToken);
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
    [DataRow("/notifications")]
    [DataRow("/import-review")]
    [DataRow("/_blazor")]
    [DataRow("/app.khy4lop6wu.css")]
    [DataRow("/Quotinator.Api.ngd3z69k33.styles.css")]
    [DataRow("/favicon.png")]
    [TestMethod]
    public async Task Unhealthy_ExemptPath_CallsNext(string path)
    {
        DatabaseHealthState health = new();
        health.MarkFailed("schema mismatch", DatabaseFaultKind.ExpectedSchemaMissing);
        DatabaseHealthGateMiddleware middleware = new(health);
        (RequestDelegate? next, Func<bool>? wasCalled) = SpyNext();

        await middleware.InvokeAsync(MakeContext(path), next);

        Assert.IsTrue(wasCalled(), $"{path} must stay reachable even when the database is unhealthy");
    }

    [TestMethod]
    public async Task Unhealthy_QuotesEndpoint_StaysGated()
    {
        DatabaseHealthState health = new();
        health.MarkFailed("schema mismatch", DatabaseFaultKind.ExpectedSchemaMissing);
        DatabaseHealthGateMiddleware middleware = new(health);
        (RequestDelegate? next, Func<bool>? wasCalled) = SpyNext();
        DefaultHttpContext context = MakeContext("/api/v1/quotes/random");

        await middleware.InvokeAsync(context, next);

        Assert.IsFalse(wasCalled(), "#263's new Blazor-route exemptions must not widen the gate for real REST data endpoints");
        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
    }
}

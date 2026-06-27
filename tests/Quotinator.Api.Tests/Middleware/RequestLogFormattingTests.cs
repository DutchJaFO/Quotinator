using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Quotinator.Api.Middleware;
using Quotinator.Api.Tests.Fakes;

namespace Quotinator.Api.Tests.Middleware;

[TestClass]
public class RequestLogFormattingTests
{
    // -------------------------------------------------------------------------
    #region Helpers

    /// <summary>
    /// Builds middleware with a sink at the given minimum level.
    /// Default is Information — Debug routes are invisible at this level, which is the
    /// correct production default and is what most tests want to assert.
    /// Pass LogEventLevel.Debug to test that debug-category routes actually emit lines.
    /// </summary>
    private static (RequestLoggingMiddleware Middleware, CaptureSink Sink) Build(
        LogEventLevel minimumLevel = LogEventLevel.Information)
    {
        var sink    = new CaptureSink();
        var serilog = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .WriteTo.Sink(sink)
            .CreateLogger();
        var logger = new SerilogLoggerFactory(serilog)
            .CreateLogger<RequestLoggingMiddleware>();
        return (new RequestLoggingMiddleware(logger), sink);
    }

    private static DefaultHttpContext MakeContext(string method, string path, string? query = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        ctx.Request.Path   = new PathString(path);
        if (query is not null)
            ctx.Request.QueryString = new QueryString(query);
        return ctx;
    }

    private static RequestDelegate Respond(int statusCode = 200) => ctx =>
    {
        ctx.Response.StatusCode = statusCode;
        return Task.CompletedTask;
    };

    private const string Prefix = "[Api - Request] ";

    private static string ExtractId(string line)
        => line[Prefix.Length..(Prefix.Length + 8)];

    #endregion

    // -------------------------------------------------------------------------
    #region Row 1 — start line emitted before response

    [TestMethod]
    public async Task StartLine_EmittedBeforeResponse()
    {
        var (middleware, sink) = Build();
        await middleware.InvokeAsync(MakeContext("GET", "/api/v1/health"), Respond());

        Assert.IsFalse(sink.Lines[0].Contains('→'),
            "Start line must not contain → — it is emitted before next() is called");
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Row 2 — end line contains status and duration

    [TestMethod]
    public async Task EndLine_ContainsStatusAndDuration()
    {
        var (middleware, sink) = Build();
        await middleware.InvokeAsync(MakeContext("GET", "/api/v1/health"), Respond(200));

        StringAssert.Contains(sink.Lines[1], "→ 200 in");
        StringAssert.Contains(sink.Lines[1], "ms");
    }

    [TestMethod]
    public async Task EndLine_ReflectsNonOkStatusCode()
    {
        var (middleware, sink) = Build();
        await middleware.InvokeAsync(MakeContext("GET", "/api/v1/quotes/0"), Respond(404));

        StringAssert.Contains(sink.Lines[1], "→ 404 in");
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Row 3 — both lines share the same correlation ID

    [TestMethod]
    public async Task BothLines_ShareCorrelationId()
    {
        var (middleware, sink) = Build();
        await middleware.InvokeAsync(MakeContext("GET", "/api/v1/health"), Respond());

        Assert.AreEqual(2, sink.Lines.Count);
        var id0 = ExtractId(sink.Lines[0]);
        var id1 = ExtractId(sink.Lines[1]);
        Assert.AreEqual(id0, id1, "Both lines must carry the same correlation ID");
        Assert.AreEqual(8,   id0.Length, "Correlation ID must be 8 hex characters");
    }

    [TestMethod]
    public async Task OverlappingRequests_HaveDistinctCorrelationIds()
    {
        // Two separate invocations must produce different IDs
        var (middleware, sink) = Build();
        await middleware.InvokeAsync(MakeContext("GET", "/api/v1/health"),       Respond());
        await middleware.InvokeAsync(MakeContext("GET", "/api/v1/quotes/random"), Respond());

        var id0 = ExtractId(sink.Lines[0]);
        var id2 = ExtractId(sink.Lines[2]);
        Assert.AreNotEqual(id0, id2, "Different requests must have different correlation IDs");
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Row 4 — string properties not quoted (Serilog {:l} specifier)

    [TestMethod]
    public async Task StringProperties_NotQuoted()
    {
        var (middleware, sink) = Build();
        await middleware.InvokeAsync(MakeContext("GET", "/api/v1/health"), Respond());

        var start = sink.Lines[0];
        Assert.IsFalse(start.Contains("\"GET\""),
            "HTTP method must not be wrapped in quotes by Serilog");
        Assert.IsFalse(start.Contains("\"/api/v1/health\""),
            "URL must not be wrapped in quotes by Serilog");
        StringAssert.Contains(start, "GET /api/v1/health");
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Row 5 — URL path and query combined; no trailing separator

    [TestMethod]
    public async Task Url_PathAndQueryCombined()
    {
        var (middleware, sink) = Build();
        await middleware.InvokeAsync(
            MakeContext("GET", "/api/v1/quotes/search", "?q=back"), Respond());

        StringAssert.Contains(sink.Lines[0], "/api/v1/quotes/search?q=back");
        Assert.IsFalse(sink.Lines[0].Contains("search\"\"?"),
            "Must not have double-quote between path and query string");
    }

    [TestMethod]
    public async Task Url_NoQuery_NoTrailingSeparator()
    {
        var (middleware, sink) = Build();
        await middleware.InvokeAsync(MakeContext("GET", "/api/v1/health"), Respond());

        Assert.IsTrue(sink.Lines[0].EndsWith("/api/v1/health"),
            "Start line must end with the path when there is no query string");
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Row 6 — all routes logged

    [TestMethod]
    public async Task AllRoutes_AreLogged()
    {
        var (middleware, sink) = Build();

        await middleware.InvokeAsync(MakeContext("GET",  "/api/v1/health"),               Respond());
        await middleware.InvokeAsync(MakeContext("POST", "/api/v1/admin/database/reseed"), Respond());

        Assert.AreEqual(4, sink.Lines.Count,
            "Each request must produce exactly 2 log lines");
        Assert.IsTrue(sink.Lines.Any(l => l.Contains("/api/v1/health")),
            "Health endpoint must appear in log");
        Assert.IsTrue(sink.Lines.Any(l => l.Contains("/api/v1/admin/database/reseed")),
            "Admin endpoint must appear in log");
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Row 7 — [Api - Request] prefix on both lines

    [TestMethod]
    public async Task BothLines_HavePrefix()
    {
        var (middleware, sink) = Build();
        await middleware.InvokeAsync(MakeContext("GET", "/api/v1/health"), Respond());

        Assert.AreEqual(2, sink.Lines.Count);
        StringAssert.StartsWith(sink.Lines[0], Prefix);
        StringAssert.StartsWith(sink.Lines[1], Prefix);
    }

    [TestMethod]
    public async Task EachRequest_ProducesExactlyTwoLines()
    {
        var (middleware, sink) = Build();
        await middleware.InvokeAsync(MakeContext("GET", "/api/v1/health"), Respond());

        Assert.AreEqual(2, sink.Lines.Count);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Row 8 — three-category tags

    [TestMethod]
    public async Task ApiRoute_UsesApiRequestTag()
    {
        var (middleware, sink) = Build(LogEventLevel.Debug);
        await middleware.InvokeAsync(MakeContext("GET", "/api/v1/quotes/random"), Respond());

        StringAssert.Contains(sink.Lines[0], "[Api - Request]");
    }

    [TestMethod]
    public async Task BlazorPage_UsesWebRequestTag()
    {
        var (middleware, sink) = Build(LogEventLevel.Debug);
        await middleware.InvokeAsync(MakeContext("GET", "/about"), Respond());

        StringAssert.Contains(sink.Lines[0], "[Web - Request]");
    }

    [TestMethod]
    public async Task CultureRoute_UsesWebRequestTag()
    {
        var (middleware, sink) = Build(LogEventLevel.Debug);
        await middleware.InvokeAsync(
            MakeContext("GET", "/Culture/Set", "?culture=nl&redirectUri=%2Fabout"), Respond());

        StringAssert.Contains(sink.Lines[0], "[Web - Request]");
    }

    [TestMethod]
    public async Task ScalarUiPage_UsesWebRequestTag()
    {
        var (middleware, sink) = Build(LogEventLevel.Debug);
        await middleware.InvokeAsync(MakeContext("GET", "/scalar/v1"), Respond());

        StringAssert.Contains(sink.Lines[0], "[Web - Request]");
    }

    [TestMethod]
    public async Task CssFile_UsesWebAssetTag()
    {
        var (middleware, sink) = Build(LogEventLevel.Debug);
        await middleware.InvokeAsync(MakeContext("GET", "/app.khy4lop6wu.css"), Respond());

        StringAssert.Contains(sink.Lines[0], "[Web - Asset]");
    }

    [TestMethod]
    public async Task JsFile_UsesWebAssetTag()
    {
        var (middleware, sink) = Build(LogEventLevel.Debug);
        await middleware.InvokeAsync(MakeContext("GET", "/scalar/scalar.js"), Respond());

        StringAssert.Contains(sink.Lines[0], "[Web - Asset]");
    }

    [TestMethod]
    public async Task BlazorFrameworkAsset_UsesWebAssetTag()
    {
        var (middleware, sink) = Build(LogEventLevel.Debug);
        await middleware.InvokeAsync(
            MakeContext("GET", "/_framework/blazor.web.ne14ti1q68.js"), Respond());

        StringAssert.Contains(sink.Lines[0], "[Web - Asset]");
    }

    [TestMethod]
    public async Task BlazorContentAsset_UsesWebAssetTag()
    {
        var (middleware, sink) = Build(LogEventLevel.Debug);
        await middleware.InvokeAsync(
            MakeContext("GET", "/_content/Toolbelt.Blazor.I18nText/i18n.js"), Respond());

        StringAssert.Contains(sink.Lines[0], "[Web - Asset]");
    }

    [TestMethod]
    public async Task SvgFile_UsesWebAssetTag()
    {
        var (middleware, sink) = Build(LogEventLevel.Debug);
        await middleware.InvokeAsync(MakeContext("GET", "/logo.svg"), Respond());

        StringAssert.Contains(sink.Lines[0], "[Web - Asset]");
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Row 9 — log levels: API at Information, web/assets at Debug

    [TestMethod]
    public async Task ApiRoute_LoggedAtInformationLevel()
    {
        var (middleware, sink) = Build(LogEventLevel.Debug);
        await middleware.InvokeAsync(MakeContext("GET", "/api/v1/health"), Respond());

        Assert.IsTrue(sink.Events.All(e => e.Level == LogEventLevel.Information),
            "API routes must log at Information level");
    }

    [TestMethod]
    public async Task WebRoute_LoggedAtDebugLevel()
    {
        var (middleware, sink) = Build(LogEventLevel.Debug);
        await middleware.InvokeAsync(MakeContext("GET", "/about"), Respond());

        Assert.IsTrue(sink.Events.All(e => e.Level == LogEventLevel.Debug),
            "Blazor pages must log at Debug level");
    }

    [TestMethod]
    public async Task StaticAsset_LoggedAtDebugLevel()
    {
        var (middleware, sink) = Build(LogEventLevel.Debug);
        await middleware.InvokeAsync(MakeContext("GET", "/app.css"), Respond());

        Assert.IsTrue(sink.Events.All(e => e.Level == LogEventLevel.Debug),
            "Static assets must log at Debug level");
    }

    [TestMethod]
    public async Task WebRoute_NotVisibleAtInformationLevel()
    {
        // Default Build() uses MinimumLevel.Information — web routes must produce no output
        var (middleware, sink) = Build();
        await middleware.InvokeAsync(MakeContext("GET", "/about"), Respond());

        Assert.AreEqual(0, sink.Lines.Count,
            "Blazor pages must not appear in the log at Information level");
    }

    [TestMethod]
    public async Task StaticAsset_NotVisibleAtInformationLevel()
    {
        var (middleware, sink) = Build();
        await middleware.InvokeAsync(MakeContext("GET", "/logo.svg"), Respond());

        Assert.AreEqual(0, sink.Lines.Count,
            "Static assets must not appear in the log at Information level");
    }

    #endregion
}

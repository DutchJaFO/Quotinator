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
    /// Default is Debug — every request/response line is logged at Debug regardless of category
    /// (#244), so Debug is what most tests need to see any output at all.
    /// Pass LogEventLevel.Information to assert that request logging stays invisible at the
    /// production default level.
    /// </summary>
    private static (RequestLoggingMiddleware Middleware, CaptureSink Sink) Build(
        LogEventLevel minimumLevel = LogEventLevel.Debug)
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

        Assert.Contains("→ 200 in", sink.Lines[1]);
        Assert.Contains("ms", sink.Lines[1]);
    }

    [TestMethod]
    public async Task EndLine_ReflectsNonOkStatusCode()
    {
        var (middleware, sink) = Build();
        await middleware.InvokeAsync(MakeContext("GET", "/api/v1/quotes/0"), Respond(404));

        Assert.Contains("→ 404 in", sink.Lines[1]);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Row 3 — both lines share the same correlation ID

    [TestMethod]
    public async Task BothLines_ShareCorrelationId()
    {
        var (middleware, sink) = Build();
        await middleware.InvokeAsync(MakeContext("GET", "/api/v1/health"), Respond());

        Assert.HasCount(2, sink.Lines);
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
        Assert.DoesNotContain("\"GET\"", start,
            "HTTP method must not be wrapped in quotes by Serilog");
        Assert.DoesNotContain("\"/api/v1/health\"", start,
            "URL must not be wrapped in quotes by Serilog");
        Assert.Contains("GET /api/v1/health", start);
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

        Assert.Contains("/api/v1/quotes/search?q=back", sink.Lines[0]);
        Assert.DoesNotContain("search\"\"?", sink.Lines[0],
            "Must not have double-quote between path and query string");
    }

    [TestMethod]
    public async Task Url_NoQuery_NoTrailingSeparator()
    {
        var (middleware, sink) = Build();
        await middleware.InvokeAsync(MakeContext("GET", "/api/v1/health"), Respond());

        Assert.EndsWith("/api/v1/health", sink.Lines[0],
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

        Assert.HasCount(4, sink.Lines,
            "Each request must produce exactly 2 log lines");
        Assert.Contains(l => l.Contains("/api/v1/health"), sink.Lines,
            "Health endpoint must appear in log");
        Assert.Contains(l => l.Contains("/api/v1/admin/database/reseed"), sink.Lines,
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

        Assert.HasCount(2, sink.Lines);
        Assert.StartsWith(Prefix, sink.Lines[0]);
        Assert.StartsWith(Prefix, sink.Lines[1]);
    }

    [TestMethod]
    public async Task EachRequest_ProducesExactlyTwoLines()
    {
        var (middleware, sink) = Build();
        await middleware.InvokeAsync(MakeContext("GET", "/api/v1/health"), Respond());

        Assert.HasCount(2, sink.Lines);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Row 8 — three-category tags

    [TestMethod]
    public async Task ApiRoute_UsesApiRequestTag()
    {
        var (middleware, sink) = Build(LogEventLevel.Debug);
        await middleware.InvokeAsync(MakeContext("GET", "/api/v1/quotes/random"), Respond());

        Assert.Contains("[Api - Request]", sink.Lines[0]);
    }

    [TestMethod]
    public async Task BlazorPage_UsesWebRequestTag()
    {
        var (middleware, sink) = Build(LogEventLevel.Debug);
        await middleware.InvokeAsync(MakeContext("GET", "/about"), Respond());

        Assert.Contains("[Web - Request]", sink.Lines[0]);
    }

    [TestMethod]
    public async Task CultureRoute_UsesWebRequestTag()
    {
        var (middleware, sink) = Build(LogEventLevel.Debug);
        await middleware.InvokeAsync(
            MakeContext("GET", "/Culture/Set", "?culture=nl&redirectUri=%2Fabout"), Respond());

        Assert.Contains("[Web - Request]", sink.Lines[0]);
    }

    [TestMethod]
    public async Task ScalarUiPage_UsesWebRequestTag()
    {
        var (middleware, sink) = Build(LogEventLevel.Debug);
        await middleware.InvokeAsync(MakeContext("GET", "/scalar/v1"), Respond());

        Assert.Contains("[Web - Request]", sink.Lines[0]);
    }

    [TestMethod]
    public async Task CssFile_UsesWebAssetTag()
    {
        var (middleware, sink) = Build(LogEventLevel.Debug);
        await middleware.InvokeAsync(MakeContext("GET", "/app.khy4lop6wu.css"), Respond());

        Assert.Contains("[Web - Asset]", sink.Lines[0]);
    }

    [TestMethod]
    public async Task JsFile_UsesWebAssetTag()
    {
        var (middleware, sink) = Build(LogEventLevel.Debug);
        await middleware.InvokeAsync(MakeContext("GET", "/scalar/scalar.js"), Respond());

        Assert.Contains("[Web - Asset]", sink.Lines[0]);
    }

    [TestMethod]
    public async Task BlazorFrameworkAsset_UsesWebAssetTag()
    {
        var (middleware, sink) = Build(LogEventLevel.Debug);
        await middleware.InvokeAsync(
            MakeContext("GET", "/_framework/blazor.web.ne14ti1q68.js"), Respond());

        Assert.Contains("[Web - Asset]", sink.Lines[0]);
    }

    [TestMethod]
    public async Task BlazorContentAsset_UsesWebAssetTag()
    {
        var (middleware, sink) = Build(LogEventLevel.Debug);
        await middleware.InvokeAsync(
            MakeContext("GET", "/_content/Toolbelt.Blazor.I18nText/i18n.js"), Respond());

        Assert.Contains("[Web - Asset]", sink.Lines[0]);
    }

    [TestMethod]
    public async Task SvgFile_UsesWebAssetTag()
    {
        var (middleware, sink) = Build(LogEventLevel.Debug);
        await middleware.InvokeAsync(MakeContext("GET", "/logo.svg"), Respond());

        Assert.Contains("[Web - Asset]", sink.Lines[0]);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Row 9 — log levels: every category is Debug-only (#244)

    [TestMethod]
    public async Task ApiRoute_LoggedAtDebugLevel()
    {
        var (middleware, sink) = Build(LogEventLevel.Debug);
        await middleware.InvokeAsync(MakeContext("GET", "/api/v1/health"), Respond());

        Assert.IsTrue(sink.Events.All(e => e.Level == LogEventLevel.Debug),
            "API routes must log at Debug level — normal operation logs only the bare minimum");
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
    public async Task ApiRoute_NotVisibleAtInformationLevel()
    {
        var (middleware, sink) = Build(LogEventLevel.Information);
        await middleware.InvokeAsync(MakeContext("GET", "/api/v1/health"), Respond());

        Assert.IsEmpty(sink.Lines,
            "API routes must not appear in the log at Information level — request logging is opt-in verbosity");
    }

    [TestMethod]
    public async Task WebRoute_NotVisibleAtInformationLevel()
    {
        var (middleware, sink) = Build(LogEventLevel.Information);
        await middleware.InvokeAsync(MakeContext("GET", "/about"), Respond());

        Assert.IsEmpty(sink.Lines,
            "Blazor pages must not appear in the log at Information level");
    }

    [TestMethod]
    public async Task StaticAsset_NotVisibleAtInformationLevel()
    {
        var (middleware, sink) = Build(LogEventLevel.Information);
        await middleware.InvokeAsync(MakeContext("GET", "/logo.svg"), Respond());

        Assert.IsEmpty(sink.Lines,
            "Static assets must not appear in the log at Information level");
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Exception propagation — completion line always fires

    [TestMethod]
    public async Task CompletionLine_EmittedEvenWhenNextThrows()
    {
        var (middleware, sink) = Build();
        var ctx = MakeContext("GET", "/api/v1/quotes/random", "?yearFrom=abc");

        static Task throws(HttpContext _) => throw new BadHttpRequestException("bind failure");

        try { await middleware.InvokeAsync(ctx, throws); }
        catch (BadHttpRequestException) { /* expected — handler is not in this test */ }

        Assert.HasCount(2, sink.Lines,
            "Both start and completion lines must be emitted even when the next delegate throws");
        Assert.Contains("→", sink.Lines[1],
            "Completion line must contain the arrow separator");
    }

    #endregion
}

using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Quotinator.Data.Helpers;

namespace Quotinator.Api.Middleware;

/// <summary>Logs every HTTP request as two lines: one on arrival, one on completion.</summary>
/// <remarks>
/// Always logged at Debug — normal operation logs only the bare minimum (e.g. the startup/shutdown
/// banners); per-request detail is opt-in verbosity for when an operator is actively debugging.
/// Requests are still categorised by path for the tag prefix:
/// <list type="bullet">
///   <item><description><c>[Api - Request]</c> — REST API calls under <c>/api/</c></description></item>
///   <item><description><c>[Web - Request]</c> — Blazor pages, culture routes, OpenAPI/Scalar UI</description></item>
///   <item><description><c>[Web - Asset]</c> — static files and Blazor framework assets</description></item>
/// </list>
/// Each request gets a short random correlation ID (8 hex chars) that appears on both lines,
/// making start/end pairs unambiguous when long-running requests overlap with shorter ones.
/// String properties use the Serilog {l} literal specifier to suppress quoting in rendered output.
/// Never logs header values — X-Api-Key, Authorization, Cookie must not appear in logs.
/// </remarks>
/// <remarks>Initializes a new instance of <see cref="RequestLoggingMiddleware"/>.</remarks>
/// <param name="logger">Logger the middleware writes request/response lines to.</param>
public class RequestLoggingMiddleware(ILogger<RequestLoggingMiddleware> logger) : IMiddleware
{
    private readonly ILogger<RequestLoggingMiddleware> _logger = logger;

    private static readonly string[] AssetPrefixes = ["/_framework/", "/_content/", "/lib/"];

    private static readonly string[] AssetExtensions =
        [".js", ".css", ".svg", ".png", ".ico", ".woff", ".woff2", ".ttf", ".map", ".webp", ".gif", ".jpg", ".jpeg"];

    /// <inheritdoc/>
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var id     = Guid.NewGuid().ToString("N")[..8];
        var method = LogSanitizer.ForLog(context.Request.Method);
        var url    = LogSanitizer.ForLog(context.Request.Path + context.Request.QueryString.Value);
        var tag    = Categorise(context.Request.Path.Value ?? string.Empty);

        _logger.LogDebug("{Tag:l} {Id:l} {Method:l} {Url:l}", tag, id, method, url);

        var sw = Stopwatch.StartNew();
        try
        {
            await next(context);
        }
        finally
        {
            sw.Stop();
            _logger.LogDebug("{Tag:l} {Id:l} {Method:l} {Url:l} → {Status} in {Ms}ms",
                tag, id, method, url, context.Response.StatusCode, sw.ElapsedMilliseconds);
        }
    }

    #region Private

    private static string Categorise(string path)
    {
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            return "[Api - Request]";

        if (IsStaticAsset(path))
            return "[Web - Asset]";

        return "[Web - Request]";
    }

    private static bool IsStaticAsset(string path)
    {
        foreach (var prefix in AssetPrefixes)
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;

        var dot = path.LastIndexOf('.');
        if (dot >= 0)
        {
            var ext = path[dot..];
            foreach (var assetExt in AssetExtensions)
                if (ext.Equals(assetExt, StringComparison.OrdinalIgnoreCase))
                    return true;
        }

        return false;
    }

    #endregion
}

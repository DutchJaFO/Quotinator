using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Quotinator.Api.Middleware;

/// <summary>Logs every HTTP request as two lines: one on arrival, one on completion.</summary>
/// <remarks>
/// Requests are categorised by path and logged at different levels:
/// <list type="bullet">
///   <item><description><c>[Api - Request]</c> at Information — REST API calls under <c>/api/</c></description></item>
///   <item><description><c>[Web - Request]</c> at Debug — Blazor pages, culture routes, OpenAPI/Scalar UI</description></item>
///   <item><description><c>[Web - Asset]</c> at Debug — static files and Blazor framework assets</description></item>
/// </list>
/// Each request gets a short random correlation ID (8 hex chars) that appears on both lines,
/// making start/end pairs unambiguous when long-running requests overlap with shorter ones.
/// String properties use the Serilog {l} literal specifier to suppress quoting in rendered output.
/// Never logs header values — X-Api-Key, Authorization, Cookie must not appear in logs.
/// </remarks>
public class RequestLoggingMiddleware : IMiddleware
{
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    private static readonly string[] AssetPrefixes = ["/_framework/", "/_content/", "/lib/"];

    private static readonly string[] AssetExtensions =
        [".js", ".css", ".svg", ".png", ".ico", ".woff", ".woff2", ".ttf", ".map", ".webp", ".gif", ".jpg", ".jpeg"];

    /// <summary>Initializes a new instance of <see cref="RequestLoggingMiddleware"/>.</summary>
    public RequestLoggingMiddleware(ILogger<RequestLoggingMiddleware> logger)
        => _logger = logger;

    /// <inheritdoc/>
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var id  = Guid.NewGuid().ToString("N")[..8];
        var url = SanitiseForLog(context.Request.Path + context.Request.QueryString.Value);
        var (tag, isDebug) = Categorise(context.Request.Path.Value ?? string.Empty);

        Log(isDebug, "{Tag:l} {Id:l} {Method:l} {Url:l}",
            tag, id, context.Request.Method, url);

        var sw = Stopwatch.StartNew();
        try
        {
            await next(context);
        }
        finally
        {
            sw.Stop();
            Log(isDebug, "{Tag:l} {Id:l} {Method:l} {Url:l} → {Status} in {Ms}ms",
                tag, id, context.Request.Method, url,
                context.Response.StatusCode, sw.ElapsedMilliseconds);
        }
    }

    #region Private

    private void Log(bool isDebug, string template, params object[] args)
    {
        if (isDebug)
            _logger.LogDebug(template, args);
        else
            _logger.LogInformation(template, args);
    }

    private static (string Tag, bool IsDebug) Categorise(string path)
    {
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            return ("[Api - Request]", false);

        if (IsStaticAsset(path))
            return ("[Web - Asset]", true);

        return ("[Web - Request]", true);
    }

    private static string SanitiseForLog(string value)
        => value.Replace('\r', ' ').Replace('\n', ' ');

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

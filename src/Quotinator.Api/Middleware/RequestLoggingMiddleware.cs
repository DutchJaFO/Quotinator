using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Quotinator.Api.Middleware;

/// <summary>Logs every HTTP request as two lines: one on arrival, one on completion.</summary>
/// <remarks>
/// Each request gets a short random correlation ID (8 hex chars) that appears on both lines,
/// making start/end pairs unambiguous when long-running requests overlap with shorter ones.
/// String properties use the Serilog {l} literal specifier to suppress quoting in rendered output.
/// Never logs header values — X-Api-Key, Authorization, Cookie must not appear in logs.
/// </remarks>
public class RequestLoggingMiddleware : IMiddleware
{
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    /// <summary>Initializes a new instance of <see cref="RequestLoggingMiddleware"/>.</summary>
    public RequestLoggingMiddleware(ILogger<RequestLoggingMiddleware> logger)
        => _logger = logger;

    /// <inheritdoc/>
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var id  = Guid.NewGuid().ToString("N")[..8];
        var url = context.Request.Path + context.Request.QueryString.Value;

        _logger.LogInformation("[Api - Request] {Id:l} {Method:l} {Url:l}",
            id, context.Request.Method, url);

        var sw = Stopwatch.StartNew();
        await next(context);
        sw.Stop();

        _logger.LogInformation("[Api - Request] {Id:l} {Method:l} {Url:l} → {Status} in {Ms}ms",
            id, context.Request.Method, url,
            context.Response.StatusCode, sw.ElapsedMilliseconds);
    }
}

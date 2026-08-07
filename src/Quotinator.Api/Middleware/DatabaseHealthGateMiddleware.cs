using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Quotinator.Api.Startup;

namespace Quotinator.Api.Middleware;

/// <summary>
/// Degrades every request to a clear <c>503</c> once <see cref="DatabaseHealthState"/> records a
/// failed startup database initialisation — except health/version/admin traffic and static assets,
/// which stay reachable so the app never becomes fully unreachable (in particular, so
/// <c>POST /api/v1/admin/database/reset</c> — the one endpoint actually capable of resolving the
/// underlying schema/version mismatch — can still be called). Registered early in the pipeline so
/// a degraded request never reaches a handler that would otherwise throw the same raw exception
/// DatabaseHealthState was populated from, request after request.
/// </summary>
/// <remarks>Initializes a new instance of <see cref="DatabaseHealthGateMiddleware"/>.</remarks>
/// <param name="health">Shared startup database-health state consulted on every request.</param>
internal class DatabaseHealthGateMiddleware(DatabaseHealthState health) : IMiddleware
{
    private static readonly string[] ExemptPrefixes =
    [
        "/api/v1/health", "/api/v1/version", "/api/v1/admin",
        "/openapi", "/scalar",
        "/_framework/", "/_content/", "/lib/",
    ];

    private readonly DatabaseHealthState _health = health;

    /// <inheritdoc/>
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (_health.IsHealthy || IsExempt(path))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode  = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = "unavailable",
            reason = _health.FailureReason,
        }));
    }

    private static bool IsExempt(string path)
    {
        foreach (var prefix in ExemptPrefixes)
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }
}

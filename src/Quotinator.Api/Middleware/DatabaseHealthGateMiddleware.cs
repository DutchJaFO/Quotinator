using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Quotinator.Api.Startup;

namespace Quotinator.Api.Middleware;

/// <summary>
/// Degrades every request to a clear <c>503</c> once <see cref="DatabaseHealthState"/> records a
/// failed startup database initialisation — except health/version/admin traffic, static assets, and
/// (#263) the Blazor UI's own page routes and SignalR circuit, which stay reachable so the app never
/// becomes fully unreachable (in particular, so <c>POST /api/v1/admin/database/reset</c> — the one
/// endpoint actually capable of resolving the underlying schema/version mismatch — can still be
/// called, and so a degraded startup shows a real page instead of a bare JSON error). Registered
/// early in the pipeline so a degraded request never reaches a handler that would otherwise throw the
/// same raw exception DatabaseHealthState was populated from, request after request.
/// </summary>
/// <remarks>Initializes a new instance of <see cref="DatabaseHealthGateMiddleware"/>.</remarks>
/// <param name="health">Shared startup database-health state consulted on every request.</param>
internal class DatabaseHealthGateMiddleware(DatabaseHealthState health) : IMiddleware
{
    // #263: "/" cannot be a StartsWith prefix — every request path starts with "/", which would
    // defeat the gate entirely. It is checked as an exact match by IsExempt instead.
    private static readonly string[] ExemptPrefixes =
    [
        "/api/v1/health", "/api/v1/version", "/api/v1/admin",
        "/openapi", "/scalar",
        "/_framework/", "/_content/", "/lib/",
        // #303: /import-review is exempt for the same reason /notifications is — it is where an operator
        // sees what is unresolved, which is exactly what they need when the database is degraded.
        "/_blazor", "/rest-api", "/about", "/stats", "/notifications", "/import-review",
        "/favicon.png",
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
        if (path.Length == 0 || path == "/") return true;

        foreach (var prefix in ExemptPrefixes)
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;

        // #263: App.razor references app.css/Quotinator.Api.styles.css via ASP.NET Core's Map Static
        // Assets fingerprinting (@Assets[...]), which bakes a build-specific content hash into the
        // served filename (e.g. "/app.khy4lop6wu.css") — a literal path can never match that hash, so
        // these two are matched by shape instead. Confirmed live: without this, the degraded page
        // rendered completely unstyled because both stylesheet requests were being gated to 503.
        if (path.StartsWith("/app.", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.StartsWith("/Quotinator.Api.", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".styles.css", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}

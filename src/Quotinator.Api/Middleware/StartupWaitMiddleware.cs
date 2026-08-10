using Microsoft.AspNetCore.Http;
using Quotinator.Api.Startup;
using Quotinator.Constants.Api;
using Quotinator.Constants.Routes;
using Quotinator.Core.Services;

namespace Quotinator.Api.Middleware;

/// <summary>
/// Serves a self-contained, auto-refreshing "starting up" HTML page for every request while
/// <see cref="StartupPhaseState.IsComplete"/> is <c>false</c> — except <c>/api/v1/health</c> and
/// <c>/api/v1/version</c>, which report their own distinct "starting" state instead (#280). Plain
/// HTML, no external assets, no Blazor circuit — matching the existing precedent of the
/// language-selector's static-SSR form working without one. Registered after
/// <c>UseRequestLocalization()</c> so <see cref="IApiLocalizer"/> resolves the page's text from the
/// request's own <c>Accept-Language</c>, and before <c>UseRateLimiter()</c> so a polling wait page
/// never burns the caller's rate-limit budget for when the app actually becomes ready.
/// </summary>
/// <remarks>Initializes a new instance of <see cref="StartupWaitMiddleware"/>.</remarks>
/// <param name="phase">Shared startup-completion state consulted on every request.</param>
/// <param name="localizer">Resolves the wait page's heading/body text for the current request's culture.</param>
internal sealed class StartupWaitMiddleware(StartupPhaseState phase, IApiLocalizer localizer) : IMiddleware
{
    private static readonly string[] ExemptPrefixes = [ApiRoutes.Health, ApiRoutes.Version];

    private readonly StartupPhaseState _phase = phase;
    private readonly IApiLocalizer _localizer = localizer;

    /// <inheritdoc/>
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (_phase.IsComplete || IsExempt(path))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode  = StatusCodes.Status200OK;
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync($$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta http-equiv="refresh" content="2">
            <title>{{_localizer[ApiMessages.StartupWaitHeading]}}</title>
            <style>
              body { font-family: system-ui, sans-serif; display: flex; align-items: center; justify-content: center; height: 100vh; margin: 0; background: #1a1a1a; color: #eee; }
              main { text-align: center; max-width: 32rem; padding: 2rem; }
              h1 { font-size: 1.5rem; margin-bottom: 0.75rem; }
              p { opacity: 0.8; }
            </style>
            </head>
            <body>
            <main>
            <h1>{{_localizer[ApiMessages.StartupWaitHeading]}}</h1>
            <p>{{_localizer[ApiMessages.StartupWaitBody]}}</p>
            </main>
            </body>
            </html>
            """);
    }

    private static bool IsExempt(string path)
    {
        foreach (var prefix in ExemptPrefixes)
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }
}

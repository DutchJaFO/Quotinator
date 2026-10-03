using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Quotinator.Api.Startup;
using Quotinator.Constants.Api;
using Quotinator.Constants.Routes;
using Quotinator.Core.Services;

namespace Quotinator.Api.Middleware;

/// <summary>
/// Refuses every request with <c>503</c> while <see cref="StartupPhaseState.IsComplete"/> is
/// <c>false</c>, except <c>/api/v1/health</c>, which reports its own distinct "starting" state
/// instead (#280) and is the readiness contract an external caller polls.
/// <para>
/// The status is a refusal and the body is whatever the caller asked for (#419): a browser gets the
/// self-contained, auto-refreshing "starting up" page, anything else gets problem details. This served
/// <c>200</c> with that page to everyone until then, so a caller that posted a reset during startup was
/// told it had succeeded and handed a web page, while nothing had been reset. A <c>200</c> carrying
/// content the caller did not ask for breaks its expectations whatever the method, which is why the
/// method does not decide the status and <c>/api/v1/version</c> is no longer exempt either: it cannot
/// report complete version information before startup finishes.
/// </para>
/// <para>
/// Plain HTML, no external assets, no Blazor circuit, matching the existing precedent of the
/// language-selector's static-SSR form working without one. Registered after
/// <c>UseRequestLocalization()</c> so <see cref="IApiLocalizer"/> resolves the page's text from the
/// request's own <c>Accept-Language</c>, and before <c>UseRateLimiter()</c> so a polling wait page
/// never burns the caller's rate-limit budget for when the app actually becomes ready.
/// </para>
/// </summary>
/// <remarks>Initializes a new instance of <see cref="StartupWaitMiddleware"/>.</remarks>
/// <param name="phase">Shared startup-completion state consulted on every request.</param>
/// <param name="localizer">Resolves the wait page's heading/body text for the current request's culture.</param>
internal sealed class StartupWaitMiddleware(StartupPhaseState phase, IApiLocalizer localizer) : IMiddleware
{
    // #419: health alone. It is the readiness contract an external caller polls, so it is the one path
    // that must answer while the gate is closed. /api/v1/version was exempt until then and is not any
    // more: it cannot report complete version information or a ready state before startup finishes, and
    // a caller could otherwise reach for it as a status endpoint in health's place.
    private static readonly string[] ExemptPrefixes = [ApiRoutes.Health];

    private readonly StartupPhaseState _phase = phase;
    private readonly IApiLocalizer _localizer = localizer;

    /// <inheritdoc/>
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        string path = context.Request.Path.Value ?? string.Empty;

        if (_phase.IsComplete || IsExempt(path))
        {
            await next(context);
            return;
        }

        // #419: a refusal, not a success. This answered 200 for every gated request whatever its method,
        // so a caller that posted a reset during startup was handed this page with a success status and
        // told nothing had gone wrong, while nothing had been reset either. A 200 carrying content the
        // caller did not ask for breaks its expectations whatever the method, so the method does not
        // decide the status; only the body differs, by what the caller said it accepts.
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.RetryAfter = "2";

        if (!WantsHtml(context.Request))
        {
            // Serialized from the type rather than assembled as text, per CLAUDE.md's JSON policy.
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title  = _localizer[ApiMessages.StartupWaitHeading],
                Detail = _localizer[ApiMessages.StartupWaitBody],
            }));
            return;
        }

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

    /// <summary>
    /// Whether this caller asked for a page rather than data. A browser navigating here should still get
    /// the self-contained, auto-refreshing wait page; anything else gets a refusal it can read.
    /// </summary>
    /// <param name="request">The request whose <c>Accept</c> header decides.</param>
    private static bool WantsHtml(HttpRequest request) =>
        request.Headers.Accept.Any(value => value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true);

    private static bool IsExempt(string path)
    {
        foreach (string prefix in ExemptPrefixes)
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }
}

using Microsoft.AspNetCore.Diagnostics;
using Quotinator.Logging;

namespace Quotinator.Api.Middleware;

/// <summary>
/// Logs at Critical that an exception escaped a request, then declines to handle it so the
/// exception-handler middleware still produces its own response and its own log line (#397).
/// Registered last, after every handler that genuinely handles something.
/// </summary>
/// <param name="logger">The logger to write the Critical line to.</param>
internal sealed class UnhandledRequestExceptionHandler(ILogger<UnhandledRequestExceptionHandler> logger) : IExceptionHandler
{
    /// <inheritdoc/>
    public ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogExceptionNotHandled(exception, "request");

        // Declining deliberately: every handler ahead of this one passed on the exception, so nothing
        // handled it. Returning false leaves the middleware to produce the response and its own line,
        // and keeps this handler's only job to reporting.
        return ValueTask.FromResult(false);
    }
}

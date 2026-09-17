using Microsoft.AspNetCore.Diagnostics;

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
        _ = logger;
        throw new NotImplementedException();
    }
}

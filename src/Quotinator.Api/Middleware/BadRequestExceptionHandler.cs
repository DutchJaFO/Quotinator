using Microsoft.AspNetCore.Diagnostics;

namespace Quotinator.Api.Middleware;

/// <summary>Maps <see cref="BadHttpRequestException"/> from parameter binding failures to a 400 response.</summary>
/// <remarks>
/// ASP.NET Core throws <see cref="BadHttpRequestException"/> with StatusCode=400 when a query
/// parameter cannot be converted to its declared type (e.g. "1981x" for an int?). Without this
/// handler, UseExceptionHandler() falls through to 500 even though the fault is the caller's.
/// Registered before AddProblemDetails() so it runs first in the IExceptionHandler chain.
/// </remarks>
internal sealed class BadRequestExceptionHandler : IExceptionHandler
{
    /// <inheritdoc/>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException bad)
            return false;

        context.Response.StatusCode = bad.StatusCode;
        await Results.Problem(
            detail: bad.Message,
            statusCode: bad.StatusCode)
            .ExecuteAsync(context);

        return true;
    }
}

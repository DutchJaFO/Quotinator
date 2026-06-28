using Microsoft.AspNetCore.Diagnostics;
using Quotinator.Constants.Api;
using Quotinator.Core.Services;

namespace Quotinator.Api.Middleware;

/// <summary>Maps <see cref="BadHttpRequestException"/> from parameter binding failures to a 422 response.</summary>
/// <remarks>
/// Safety net for any <see cref="BadHttpRequestException"/> that escapes the normal validation path.
/// The primary year-param case is handled at the point of origin by <c>TryParseYear</c> so this
/// handler fires only for unexpected binding failures on other parameter types.
/// Registered before AddProblemDetails() so it runs first in the IExceptionHandler chain.
/// </remarks>
internal sealed class BadRequestExceptionHandler(IApiLocalizer localizer) : IExceptionHandler
{
    /// <inheritdoc/>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException)
            return false;

        context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        await Results.Problem(
            detail: localizer[ApiMessages.NumericParameterInvalid],
            statusCode: StatusCodes.Status422UnprocessableEntity)
            .ExecuteAsync(context);

        return true;
    }
}

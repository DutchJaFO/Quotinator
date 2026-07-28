using Quotinator.Constants.Api;
using Quotinator.Core.Services;

namespace Quotinator.Api.Endpoints.Shared;

/// <summary>Shared page/pageSize parsing for every paginated endpoint — implements #183's contract.</summary>
/// <remarks>
/// <c>page</c>/<c>pageSize</c> are declared <c>string?</c> and parsed here rather than bound as
/// <c>int</c>, for the same reason <c>CLAUDE.md</c>'s "Numeric query parameter binding pattern"
/// documents for the year params: a non-nullable <c>int</c> binding failure throws
/// <see cref="BadHttpRequestException"/>, which reaches the generic
/// <see cref="Quotinator.Api.Middleware.BadRequestExceptionHandler"/> safety net and produces the
/// wrong (too generic) detail message instead of one naming the specific parameter.
/// </remarks>
internal static class PaginationParsing
{
    /// <summary>
    /// Parses <paramref name="page"/>/<paramref name="pageSize"/>, or populates <paramref name="error"/>
    /// with a 422 naming the specific failing parameter. <c>pageSize = 0</c> is valid — #183's "every
    /// row as one page" — and deliberately bypasses <see cref="QueryParamDefaults.PageSizeMax"/>.
    /// </summary>
    internal static bool TryParse(
        string? page, string? pageSize, IApiLocalizer localizer,
        out int parsedPage, out int parsedPageSize, out IResult? error)
    {
        parsedPage     = QueryParamDefaults.Page;
        parsedPageSize = QueryParamDefaults.PageSize;
        error          = null;

        if (page is not null && (!int.TryParse(page, out parsedPage) || parsedPage < 1))
        {
            error = Results.Problem(
                detail: localizer[ApiMessages.PageOutOfRange],
                statusCode: StatusCodes.Status422UnprocessableEntity);
            return false;
        }

        if (pageSize is not null && (!int.TryParse(pageSize, out parsedPageSize)
                                      || parsedPageSize < 0 || parsedPageSize > QueryParamDefaults.PageSizeMax))
        {
            error = Results.Problem(
                detail: localizer[ApiMessages.PageSizeOutOfRange],
                statusCode: StatusCodes.Status422UnprocessableEntity);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns a 422 with a detail distinct from <see cref="TryParse"/>'s when <paramref name="page"/>
    /// is beyond the last page. Can only run after the query, once <paramref name="totalPages"/> is
    /// known — a genuinely empty result set (<c>totalPages == 0</c>) is not "beyond the last page".
    /// </summary>
    internal static IResult? ValidatePageBeyondLast(int page, int totalPages, IApiLocalizer localizer)
        => totalPages > 0 && page > totalPages
            ? Results.Problem(
                detail: localizer[ApiMessages.PageBeyondLastPage],
                statusCode: StatusCodes.Status422UnprocessableEntity)
            : null;
}

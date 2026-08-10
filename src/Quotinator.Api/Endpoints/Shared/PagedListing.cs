using Quotinator.Core.Services;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Api.Endpoints.Shared;

/// <summary>
/// Shared <c>GetAll</c> skeleton for the masterdata entities that use the plain repository pattern —
/// extracted from #281's investigation: <c>parse → query → validate-beyond-last → map → wrap</c>,
/// previously duplicated across 7 endpoint files. Not used by <c>ConversationEndpoints</c>, which
/// stays on its own <see cref="IQuoteService"/>-based pattern (ADR 017/#285).
/// </summary>
internal static class PagedListing
{
    /// <summary>
    /// Parses <paramref name="page"/>/<paramref name="pageSize"/> (per #183's pagination contract),
    /// queries a page via <paramref name="repository"/>, validates the page isn't beyond the last one,
    /// then maps the page's items through <paramref name="mapItemsAsync"/> and wraps the result in a
    /// <see cref="PagedItems{T}"/>. Returns the first 422 encountered, or 200 with the mapped page.
    /// </summary>
    internal static async Task<IResult> GetAllAsync<TEntity, TResponse>(
        string? page, string? pageSize, IApiLocalizer localizer,
        IListableRepository<TEntity> repository,
        Func<IReadOnlyList<TEntity>, Task<IReadOnlyList<TResponse>>> mapItemsAsync)
        where TEntity : RecordBase
    {
        if (!PaginationParsing.TryParse(page, pageSize, localizer, out var pageValue, out var pageSizeValue, out var pageError))
            return pageError!;

        var result = await repository.GetPageAsync(pageValue, pageSizeValue);

        var beyondLast = PaginationParsing.ValidatePageBeyondLast(pageValue, result.TotalPages, localizer);
        if (beyondLast is not null)
            return beyondLast;

        var items = await mapItemsAsync(result.Items);
        return Results.Ok(new PagedItems<TResponse>(items, result.Page, result.PageSize, result.TotalCount));
    }
}

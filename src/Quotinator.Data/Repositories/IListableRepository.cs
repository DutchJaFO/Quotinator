using Quotinator.Data.Models;

namespace Quotinator.Data.Repositories;

/// <summary>
/// Extends <see cref="IRepository{T}"/> with paginated listing.
/// Implement this interface on repositories for entity types that need a "list all" capability.
/// </summary>
/// <typeparam name="T">Entity type.</typeparam>
public interface IListableRepository<T> : IRepository<T> where T : Models.RecordBase
{
    /// <summary>
    /// Returns a page of active (non-deleted) records, plus the total active count across all pages.
    /// </summary>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Rows per page. <c>0</c> means every row, as a single page — <see cref="PagedItems{T}.PageSize"/> reports the effective count actually returned.</param>
    /// <param name="orderBy">
    /// Sort columns, applied in order. Defaults to <c>DateCreated</c> ascending when
    /// <see langword="null"/> or empty. <c>Id</c> is always appended last as a tiebreaker.
    /// </param>
    /// <param name="unitOfWork">Optional. When supplied, the query runs on the unit of work's connection and transaction.</param>
    Task<PagedItems<T>> GetPageAsync(
        int page, int pageSize, IReadOnlyList<SortColumn>? orderBy = null, IUnitOfWork? unitOfWork = null);
}

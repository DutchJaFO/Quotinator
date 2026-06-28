using Quotinator.Data.Models;

namespace Quotinator.Data.Repositories;

/// <summary>
/// Repository for a parent entity that has exactly one paired detail record.
/// Write operations are handled via <see cref="AggregateRepository{TParent,TChild}"/>;
/// this interface adds the read side.
/// </summary>
/// <typeparam name="TParent">The primary entity type.</typeparam>
/// <typeparam name="TDetail">The detail entity type paired one-to-one with the parent.</typeparam>
public interface IOneToOneRepository<TParent, TDetail> : IRepository<TParent>
    where TParent : RecordBase
    where TDetail : RecordBase
{
    /// <summary>
    /// Returns the active detail record paired with <paramref name="parentId"/>,
    /// or <c>null</c> when no detail exists or the detail has been soft-deleted.
    /// </summary>
    /// <param name="parentId">The identifier of the parent record.</param>
    /// <param name="unitOfWork">Optional. When supplied, the query runs on the unit of work's connection and transaction.</param>
    Task<TDetail?> GetDetailAsync(Guid parentId, IUnitOfWork? unitOfWork = null);
}

using Quotinator.Data.Models;

namespace Quotinator.Data.Repositories;

/// <summary>Generic repository for a single database entity type.</summary>
/// <typeparam name="T">Entity type. Must inherit <see cref="RecordBase"/>.</typeparam>
public interface IRepository<T> where T : RecordBase
{
    /// <summary>
    /// Returns the entity with the given <paramref name="id"/>,
    /// or <c>null</c> if it does not exist or has been soft-deleted.
    /// </summary>
    /// <param name="id">The identifier of the entity to retrieve.</param>
    /// <param name="unitOfWork">Optional. When supplied, the query runs on the unit of work's connection and transaction.</param>
    Task<T?> GetByIdAsync(Guid id, IUnitOfWork? unitOfWork = null);

    /// <summary>Inserts a new entity into the database.</summary>
    /// <param name="entity">The entity to insert.</param>
    /// <param name="unitOfWork">Optional. When supplied, the insert participates in the unit of work's transaction.</param>
    Task InsertAsync(T entity, IUnitOfWork? unitOfWork = null);

    /// <summary>
    /// Persists changes to an existing entity.
    /// Sets <see cref="RecordBase.DateModified"/> to the current UTC time before writing.
    /// </summary>
    /// <param name="entity">The entity to update.</param>
    /// <param name="unitOfWork">Optional. When supplied, the update participates in the unit of work's transaction.</param>
    Task UpdateAsync(T entity, IUnitOfWork? unitOfWork = null);

    /// <summary>
    /// Soft-deletes the entity with the given <paramref name="id"/>.
    /// Sets <see cref="RecordBase.IsDeleted"/>, <see cref="RecordBase.DateDeleted"/>,
    /// and <see cref="RecordBase.DateModified"/>. No-op when the entity does not exist
    /// or is already deleted.
    /// </summary>
    /// <param name="id">The identifier of the entity to soft-delete.</param>
    /// <param name="unitOfWork">Optional. When supplied, the soft-delete participates in the unit of work's transaction.</param>
    Task SoftDeleteAsync(Guid id, IUnitOfWork? unitOfWork = null);
}

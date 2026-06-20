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
    Task<T?> GetByIdAsync(Guid id);

    /// <summary>Inserts a new entity into the database.</summary>
    Task InsertAsync(T entity);

    /// <summary>
    /// Persists changes to an existing entity.
    /// Sets <see cref="RecordBase.DateModified"/> to the current UTC time before writing.
    /// </summary>
    Task UpdateAsync(T entity);

    /// <summary>
    /// Soft-deletes the entity with the given <paramref name="id"/>.
    /// Sets <see cref="RecordBase.IsDeleted"/>, <see cref="RecordBase.DateDeleted"/>,
    /// and <see cref="RecordBase.DateModified"/>. No-op when the entity does not exist
    /// or is already deleted.
    /// </summary>
    Task SoftDeleteAsync(Guid id);
}

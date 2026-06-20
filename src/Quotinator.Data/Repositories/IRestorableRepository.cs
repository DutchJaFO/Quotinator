namespace Quotinator.Data.Repositories;

/// <summary>
/// Extends <see cref="IRepository{T}"/> with operations for inspecting and reversing soft-deletes,
/// and for permanently removing soft-deleted records.
/// Implement this interface on repositories for entity types that offer an undo-delete workflow.
/// </summary>
/// <typeparam name="T">Entity type.</typeparam>
public interface IRestorableRepository<T> : IRepository<T> where T : Models.RecordBase
{
    /// <summary>
    /// Returns all records in the table that are currently soft-deleted.
    /// </summary>
    /// <param name="unitOfWork">Optional. When supplied, the query runs on the unit of work's connection and transaction.</param>
    Task<IReadOnlyList<T>> GetDeletedAsync(IUnitOfWork? unitOfWork = null);

    /// <summary>
    /// Reverses a soft-delete, making the record visible again via <see cref="IRepository{T}.GetByIdAsync"/>.
    /// No-op if the record is not found or is already active.
    /// </summary>
    /// <param name="id">The identifier of the record to restore.</param>
    /// <param name="unitOfWork">Optional. When supplied, the restore participates in the unit of work's transaction.</param>
    Task RestoreAsync(Guid id, IUnitOfWork? unitOfWork = null);

    /// <summary>
    /// Permanently removes a single soft-deleted record from the table.
    /// Active records are not affected — the guard <c>AND IsDeleted = 1</c> is always applied.
    /// No-op if the record is not found or is not soft-deleted.
    /// </summary>
    /// <param name="id">The identifier of the record to hard-delete.</param>
    /// <param name="unitOfWork">Optional. When supplied, the hard-delete participates in the unit of work's transaction.</param>
    Task HardDeleteAsync(Guid id, IUnitOfWork? unitOfWork = null);

    /// <summary>
    /// Permanently removes all soft-deleted records from the table.
    /// </summary>
    /// <returns>The number of rows removed.</returns>
    /// <param name="unitOfWork">Optional. When supplied, the purge participates in the unit of work's transaction.</param>
    Task<int> PurgeAsync(IUnitOfWork? unitOfWork = null);
}

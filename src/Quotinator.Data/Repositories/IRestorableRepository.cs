namespace Quotinator.Data.Repositories;

/// <summary>
/// Extends <see cref="IRepository{T}"/> with operations for inspecting and reversing soft-deletes,
/// and for permanently removing soft-deleted records.
/// Implement this interface on repositories for entity types that offer an undo-delete workflow.
/// </summary>
/// <typeparam name="T">Entity type.</typeparam>
public interface IRestorableRepository<T> : IRepository<T> where T : Models.RecordBase
{
    /// <summary>Returns all records in the table that are currently soft-deleted.</summary>
    Task<IReadOnlyList<T>> GetDeletedAsync();

    /// <summary>
    /// Reverses a soft-delete, making the record visible again via <see cref="IRepository{T}.GetByIdAsync"/>.
    /// No-op if the record is not found or is already active.
    /// </summary>
    Task RestoreAsync(Guid id);

    /// <summary>
    /// Permanently removes a single soft-deleted record from the table.
    /// Active records are not affected — the guard <c>AND IsDeleted = 1</c> is always applied.
    /// No-op if the record is not found or is not soft-deleted.
    /// </summary>
    Task HardDeleteAsync(Guid id);

    /// <summary>
    /// Permanently removes all soft-deleted records from the table.
    /// </summary>
    /// <returns>The number of rows removed.</returns>
    Task<int> PurgeAsync();
}

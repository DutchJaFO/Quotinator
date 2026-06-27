using Quotinator.Data.Entities;
using Quotinator.Data.Models;

namespace Quotinator.Data.Repositories;

/// <summary>Repository for <see cref="ImportBatch"/> records, extending the base CRUD interface with batch-specific queries.</summary>
public interface IImportBatchRepository : IRepository<ImportBatch>
{
    /// <summary>Returns all non-deleted import batches, newest first.</summary>
    /// <param name="unitOfWork">Optional. When supplied, the query runs on the unit of work's connection and transaction.</param>
    Task<IReadOnlyList<ImportBatch>> GetAllAsync(IUnitOfWork? unitOfWork = null);

    /// <summary>Returns all non-deleted import batches of the specified type, newest first.</summary>
    /// <param name="type">The batch type to filter by.</param>
    /// <param name="unitOfWork">Optional. When supplied, the query runs on the unit of work's connection and transaction.</param>
    Task<IReadOnlyList<ImportBatch>> GetByTypeAsync(ImportBatchType type, IUnitOfWork? unitOfWork = null);

    /// <summary>Updates the <see cref="ImportBatch.RecordCount"/> for the batch with the given <paramref name="id"/>.</summary>
    /// <param name="id">The batch identifier.</param>
    /// <param name="count">The new record count.</param>
    /// <param name="unitOfWork">Optional. When supplied, the update participates in the unit of work's transaction.</param>
    Task UpdateRecordCountAsync(Guid id, int count, IUnitOfWork? unitOfWork = null);
}

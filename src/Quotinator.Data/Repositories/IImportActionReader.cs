using Quotinator.Data.Models;

namespace Quotinator.Data.Repositories;

/// <summary>Read-side operations for the import-actions log. All queries are append-only reads — the Import_Action table is never modified by this interface.</summary>
public interface IImportActionReader
{
    /// <summary>Returns a paged list of action entries, newest first, with an optional batch, status, and entity-type filter.</summary>
    Task<PagedItems<Entities.ImportActionEntity>> GetPagedAsync(string? batchId, string? status, string? entityType, int page, int pageSize);

    /// <summary>Returns a single action by Id, or <c>null</c> if none exists (#154's decide/undo/apply/discard flows).</summary>
    Task<Entities.ImportActionEntity?> GetByIdAsync(Guid id);

    /// <summary>
    /// Returns every action sharing <paramref name="batchId"/>, any status, unpaginated — #154's
    /// apply-batch readiness check needs the complete set, not a page.
    /// </summary>
    Task<IReadOnlyList<Entities.ImportActionEntity>> GetAllForBatchAsync(string batchId);
}

using Quotinator.Data.Models;

namespace Quotinator.Data.Repositories;

/// <summary>Read-side operations for the import-actions log. All queries are append-only reads — the System_ImportActions table is never modified by this interface.</summary>
public interface ISystemImportActionReader
{
    /// <summary>Returns a paged list of action entries, newest first, with an optional batch and status filter.</summary>
    Task<SystemImportActionPageResult> GetPagedAsync(string? batchId, string? status, int page, int pageSize);

    /// <summary>Returns a single action by Id, or <c>null</c> if none exists (#154's decide/undo/apply/discard flows).</summary>
    Task<Entities.SystemImportAction?> GetByIdAsync(Guid id);

    /// <summary>
    /// Returns every action sharing <paramref name="batchId"/>, any status, unpaginated — #154's
    /// apply-batch readiness check needs the complete set, not a page.
    /// </summary>
    Task<IReadOnlyList<Entities.SystemImportAction>> GetAllForBatchAsync(string batchId);
}

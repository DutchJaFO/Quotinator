using Quotinator.Data.Models;

namespace Quotinator.Data.Repositories;

/// <summary>Read-side operations for the import-conflicts log. All queries are append-only reads — the System_ImportConflicts table is never modified by this interface.</summary>
public interface ISystemImportConflictReader
{
    /// <summary>Returns a paged list of conflict entries, newest first, with an optional batch and status filter.</summary>
    Task<SystemImportConflictPageResult> GetPagedAsync(string? batchId, string? status, int page, int pageSize);

    /// <summary>Returns a single conflict by Id, or <c>null</c> if none exists (#149's decide/undo/apply flows).</summary>
    Task<Entities.SystemImportConflict?> GetByIdAsync(Guid id);

    /// <summary>
    /// Returns every conflict sharing <paramref name="batchId"/>, any status, unpaginated — #149's
    /// apply-batch readiness check needs the complete set, not a page.
    /// </summary>
    Task<IReadOnlyList<Entities.SystemImportConflict>> GetAllForBatchAsync(string batchId);
}

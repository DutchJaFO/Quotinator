using Quotinator.Data.Models;

namespace Quotinator.Data.Repositories;

/// <summary>Read-side operations for the import-conflicts log. All queries are append-only reads — the System_ImportConflicts table is never modified by this interface.</summary>
public interface ISystemImportConflictReader
{
    /// <summary>Returns a paged list of conflict entries, newest first, with an optional batch and status filter.</summary>
    Task<SystemImportConflictPageResult> GetPagedAsync(string? batchId, string? status, int page, int pageSize);
}

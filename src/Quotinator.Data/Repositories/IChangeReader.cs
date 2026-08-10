using Quotinator.Data.Entities;

namespace Quotinator.Data.Repositories;

/// <summary>Read-side operations for the change log. All queries are append-only reads — the <c>Audit_Change</c> table is never modified by this interface.</summary>
public interface IChangeReader
{
    /// <summary>Returns every change-log entry for a single entity, newest first.</summary>
    Task<IReadOnlyList<ChangeEntity>> GetHistoryAsync(string entityType, string entityId);

    /// <summary>
    /// Returns every change-log row within an optional date range, newest first, unpaginated (#249's
    /// bulk export endpoint). A <c>null</c> bound is unlimited on that side.
    /// </summary>
    Task<IReadOnlyList<ChangeEntity>> GetAllInRangeAsync(DateTime? startDate, DateTime? endDate);

    /// <summary>Matching row count for <see cref="GetAllInRangeAsync"/> — checked against the export row-count cap before assembling the response.</summary>
    Task<int> CountInRangeAsync(DateTime? startDate, DateTime? endDate);

    /// <summary>Earliest/latest <c>OccurredAt</c> across every change-log row, or <c>null</c>/<c>null</c> when the table is empty.</summary>
    Task<(DateTime? Earliest, DateTime? Latest)> GetDateRangeAsync();
}

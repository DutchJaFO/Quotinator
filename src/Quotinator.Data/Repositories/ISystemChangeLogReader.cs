using Quotinator.Data.Entities;

namespace Quotinator.Data.Repositories;

/// <summary>Read-side operations for the change log. All queries are append-only reads — the <c>System_ChangeLog</c> table is never modified by this interface.</summary>
public interface ISystemChangeLogReader
{
    /// <summary>Returns every change-log entry for a single entity, newest first.</summary>
    Task<IReadOnlyList<SystemChangeLog>> GetHistoryAsync(string entityType, string entityId);
}

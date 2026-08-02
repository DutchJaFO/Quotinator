using Quotinator.Data.Entities;

namespace Quotinator.Data.Repositories;

/// <summary>Read-side operations for the change log. All queries are append-only reads — the <c>Audit_Change</c> table is never modified by this interface.</summary>
public interface IChangeReader
{
    /// <summary>Returns every change-log entry for a single entity, newest first.</summary>
    Task<IReadOnlyList<ChangeEntity>> GetHistoryAsync(string entityType, string entityId);
}

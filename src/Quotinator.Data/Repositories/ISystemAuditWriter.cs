using System.Data;

namespace Quotinator.Data.Repositories;

/// <summary>
/// Writes and clears immutable audit entries in the <c>System_AuditEntries</c> table.
/// </summary>
/// <remarks>
/// Three <c>WriteAsync</c> overloads exist by design:
/// <list type="bullet">
/// <item>The connection overload (single entry) is used by the repository base class so the audit INSERT
/// participates in the same transaction as the triggering write operation.</item>
/// <item>The connection overload (bulk) is used by <see cref="IRepository{T}.InsertManyAsync"/> in
/// <see cref="InsertStrategy.Bulk"/> mode to write all audit entries in one SQL round-trip.</item>
/// <item>The no-connection overload is used by services (reseed, reset) where
/// the operation itself completes before the audit entry is written.</item>
/// </list>
/// <see cref="Entities.SystemAuditEntry"/> must not be constructed outside <see cref="ISystemAuditWriter"/>
/// call sites — callers should use <see cref="Entities.AuditOperation"/> constants for
/// the <c>Operation</c> field.
/// </remarks>
public interface ISystemAuditWriter
{
    /// <summary>
    /// Writes an audit entry using an existing connection and optional transaction.
    /// The INSERT participates in <paramref name="transaction"/> when supplied.
    /// </summary>
    Task WriteAsync(Entities.SystemAuditEntry entry, IDbConnection connection, IDbTransaction? transaction = null);

    /// <summary>
    /// Writes multiple audit entries using an existing connection and optional transaction.
    /// All entries participate in <paramref name="transaction"/> when supplied.
    /// </summary>
    Task WriteAsync(IReadOnlyList<Entities.SystemAuditEntry> entries, IDbConnection connection, IDbTransaction? transaction = null);

    /// <summary>
    /// Writes an audit entry by opening its own connection.
    /// Used by services where no caller connection is available.
    /// </summary>
    Task WriteAsync(Entities.SystemAuditEntry entry);

    /// <summary>
    /// Deletes all audit entries, or only those for a specific table when <paramref name="table"/> is supplied.
    /// Writes a single audit entry recording the clear operation so there is always a record that a purge occurred.
    /// </summary>
    Task ClearAsync(string? table = null);
}

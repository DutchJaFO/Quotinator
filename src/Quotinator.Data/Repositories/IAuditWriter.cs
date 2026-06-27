using System.Data;

namespace Quotinator.Data.Repositories;

/// <summary>
/// Writes and clears immutable audit entries in the <c>AuditEntries</c> table.
/// </summary>
/// <remarks>
/// Two <c>WriteAsync</c> overloads exist by design:
/// <list type="bullet">
/// <item>The connection overload is used by the repository base class so the audit INSERT
/// participates in the same transaction as the triggering write operation.</item>
/// <item>The no-connection overload is used by services (reseed, reset) where
/// the operation itself completes before the audit entry is written.</item>
/// </list>
/// <see cref="Entities.AuditEntry"/> must not be constructed outside <see cref="IAuditWriter"/>
/// call sites — callers should use <see cref="Entities.AuditOperation"/> constants for
/// the <c>Operation</c> field.
/// </remarks>
public interface IAuditWriter
{
    /// <summary>
    /// Writes an audit entry using an existing connection and optional transaction.
    /// The INSERT participates in <paramref name="transaction"/> when supplied.
    /// </summary>
    Task WriteAsync(Entities.AuditEntry entry, IDbConnection connection, IDbTransaction? transaction = null);

    /// <summary>
    /// Writes an audit entry by opening its own connection.
    /// Used by services where no caller connection is available.
    /// </summary>
    Task WriteAsync(Entities.AuditEntry entry);

    /// <summary>
    /// Deletes all audit entries, or only those for a specific table when <paramref name="table"/> is supplied.
    /// Writes a single audit entry recording the clear operation so there is always a record that a purge occurred.
    /// </summary>
    Task ClearAsync(string? table = null);
}

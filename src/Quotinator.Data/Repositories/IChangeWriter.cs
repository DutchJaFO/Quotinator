using System.Data;
using Quotinator.Data.Entities;

namespace Quotinator.Data.Repositories;

/// <summary>Writes change-log rows to the <c>Audit_Change</c> table.</summary>
/// <remarks>
/// Mirrors <see cref="IImportActionWriter"/>'s shape: the connection overload is used by
/// callers that already hold an open connection, so the write participates in the same transaction as
/// the change it describes; the no-connection overload is for callers with no connection of their own.
/// </remarks>
public interface IChangeWriter
{
    /// <summary>
    /// Writes a change-log entry using an existing connection and optional transaction.
    /// The INSERT participates in <paramref name="transaction"/> when supplied.
    /// </summary>
    Task LogAsync(ChangeEntity entry, IDbConnection connection, IDbTransaction? transaction = null);

    /// <summary>Writes a change-log entry by opening its own connection.</summary>
    Task LogAsync(ChangeEntity entry);
}

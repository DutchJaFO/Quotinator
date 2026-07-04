using System.Data;

namespace Quotinator.Data.Repositories;

/// <summary>Writes import-conflict rows to the <c>System_ImportConflicts</c> table.</summary>
/// <remarks>
/// Mirrors <see cref="ISystemAuditWriter"/>'s shape: the connection overload is used by callers
/// (e.g. the seeder) that already hold an open connection, so the write participates in the same
/// transaction as the triggering import; the no-connection overload is for callers with no
/// connection of their own.
/// </remarks>
public interface ISystemImportConflictWriter
{
    /// <summary>
    /// Writes a conflict entry using an existing connection and optional transaction.
    /// The INSERT participates in <paramref name="transaction"/> when supplied.
    /// </summary>
    Task WriteAsync(Entities.SystemImportConflict entry, IDbConnection connection, IDbTransaction? transaction = null);

    /// <summary>Writes a conflict entry by opening its own connection.</summary>
    Task WriteAsync(Entities.SystemImportConflict entry);
}

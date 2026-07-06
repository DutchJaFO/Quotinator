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

    /// <summary>
    /// Stages a per-field decision for one conflict (#149) — transitions <c>Status</c> from
    /// <c>Pending</c> to <c>Decided</c> and stores <paramref name="decisionsJson"/> in
    /// <c>MergedFields</c>. Nothing is written to any domain table. Idempotent — calling again for the
    /// same conflict overwrites the prior decision (the "change your mind before commit" path).
    /// </summary>
    Task MarkDecidedAsync(Guid id, string decisionsJson, IDbConnection connection, IDbTransaction? transaction = null);

    /// <summary>
    /// Reverts a staged decision back to <c>Pending</c> and clears the stored decision (#149's
    /// undo-before-commit). Only meaningful while <c>Status</c> is <c>Decided</c>.
    /// </summary>
    Task ClearDecisionAsync(Guid id, IDbConnection connection, IDbTransaction? transaction = null);

    /// <summary>
    /// Marks a conflict resolved once its owning batch has been applied (#149) — sets
    /// <c>ResolvedAt</c>. Called once per conflict inside the batch's shared transaction.
    /// </summary>
    Task MarkResolvedAsync(Guid id, IDbConnection connection, IDbTransaction? transaction = null);
}

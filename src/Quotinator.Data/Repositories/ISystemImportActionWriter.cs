using System.Data;
using Quotinator.Data.Entities;

namespace Quotinator.Data.Repositories;

/// <summary>Writes import-action rows to the <c>System_ImportActions</c> table.</summary>
/// <remarks>
/// The connection overload is used by callers (e.g. staging) that already hold an open connection,
/// so the write participates in the same transaction as the triggering stage/apply operation; the
/// no-connection overload is for callers with no connection of their own.
/// </remarks>
public interface ISystemImportActionWriter
{
    /// <summary>
    /// Writes an action entry using an existing connection and optional transaction.
    /// The INSERT participates in <paramref name="transaction"/> when supplied.
    /// </summary>
    Task WriteAsync(Entities.SystemImportAction entry, IDbConnection connection, IDbTransaction? transaction = null);

    /// <summary>Writes an action entry by opening its own connection.</summary>
    Task WriteAsync(Entities.SystemImportAction entry);

    /// <summary>Writes every planned action for a staged batch in one call — the bulk path staging uses.</summary>
    Task WriteManyAsync(IEnumerable<Entities.SystemImportAction> entries, IDbConnection connection, IDbTransaction? transaction = null);

    /// <summary>
    /// Stages a per-field decision for one action — transitions <c>Status</c> from
    /// <c>Pending</c> to <c>Decided</c> and stores <paramref name="decisionsJson"/> in
    /// <c>MergedFields</c>. Nothing is written to any domain table. Idempotent — calling again for the
    /// same action overwrites the prior decision (the "change your mind before apply" path).
    /// <paramref name="markCompletenessAs"/> (#165) is always written, including <c>null</c> —
    /// resubmitting a decide call without the override clears a previously-set one.
    /// </summary>
    Task MarkDecidedAsync(Guid id, string decisionsJson, CompletenessStatus? markCompletenessAs, IDbConnection connection, IDbTransaction? transaction = null);

    /// <summary>
    /// Reverts a staged decision back to <c>Pending</c> and clears the stored decision
    /// (undo-before-apply). Only meaningful while <c>Status</c> is <c>Decided</c>.
    /// </summary>
    Task ClearDecisionAsync(Guid id, IDbConnection connection, IDbTransaction? transaction = null);

    /// <summary>
    /// Marks an action applied once its owning batch has been applied — sets <c>AppliedAt</c>.
    /// Called once per action inside the batch's shared transaction.
    /// </summary>
    Task MarkAppliedAsync(Guid id, IDbConnection connection, IDbTransaction? transaction = null);

    /// <summary>
    /// Marks every action sharing <paramref name="batchId"/> discarded in one statement — sets
    /// <c>DiscardedAt</c>. The whole-batch discard operation has no per-row decision to make.
    /// </summary>
    Task MarkBatchDiscardedAsync(string batchId, IDbConnection connection, IDbTransaction? transaction = null);
}

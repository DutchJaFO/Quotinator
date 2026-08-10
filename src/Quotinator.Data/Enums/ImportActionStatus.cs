using Quotinator.Data.Entities;
using Quotinator.Data.Import;

namespace Quotinator.Data.Enums;

/// <summary>
/// The states a <see cref="ImportActionEntity"/> row can be in — a closed set defined and
/// maintained entirely by this project's own coordinator logic (<see cref="IImportActionCoordinator"/>),
/// not by any consuming project's schema. Per ADR 008, backed by a matching SQL CHECK constraint.
/// </summary>
public enum ImportActionStatus
{
    /// <summary>Genuinely ambiguous — needs an explicit decision before the owning batch can be applied.</summary>
    Pending,

    /// <summary>Auto-resolved at staging time (every Add and unambiguous Modify), or a decision has been recorded for a Pending action. Ready to apply.</summary>
    Decided,

    /// <summary>The owning batch was applied — this action's write landed on the consumer's own tables.</summary>
    Applied,

    /// <summary>The owning batch was discarded — this action was never written anywhere.</summary>
    Discarded,

    /// <summary>
    /// The target row's <c>CompletenessStatus</c> is <c>Complete</c> and this action would modify a
    /// protected field — held for explicit human review. Behaves like <see cref="Pending"/> for
    /// whole-batch apply purposes (see <see cref="ImportActionResolutionCoordinator.TryApplyBatchAsync"/>),
    /// but is a distinct, filterable status so a caller can tell the two apart.
    /// </summary>
    Blocked,

    /// <summary>
    /// A per-source conflict-resolution rule matched this field, but the rule's own recorded snapshot
    /// (#181's <c>ExistingRecord</c>/<c>IncomingRecord</c>) no longer matches this staging run's actual
    /// values — the underlying source's shape moved since the rule was authored, so silently reapplying
    /// it could produce a wrong result (#153). Behaves like <see cref="Pending"/> for whole-batch apply
    /// purposes, same as <see cref="Blocked"/>, but is a distinct, filterable status so a caller can
    /// tell "needs a decision because no rule matched" apart from "needs a decision because its rule
    /// went stale."
    /// </summary>
    Stale
}

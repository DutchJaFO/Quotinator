using Quotinator.Data.Entities;

namespace Quotinator.Data.Import;

/// <summary>
/// Generic completeness-status gating and transition logic (#165). Operates only over
/// <see cref="CompletenessStatus"/> and field-name strings, so this project has no dependency on any
/// specific domain schema — callers supply their own changed-field set and current status.
/// </summary>
public static class CompletenessGuard
{
    /// <summary>
    /// Whether an attempt to change <paramref name="changedFields"/> on a row currently at
    /// <paramref name="status"/> must be held for explicit human review instead of applied
    /// automatically. True only when <paramref name="status"/> is <see cref="CompletenessStatus.Complete"/> —
    /// a <see cref="CompletenessStatus.NeedsReview"/> row hasn't been human-confirmed yet and stays
    /// freely correctable. <paramref name="changedFields"/> is accepted for a future per-field
    /// exemption list; today any non-empty change to a <c>Complete</c> row blocks.
    /// </summary>
    public static bool ShouldBlock(CompletenessStatus status, IReadOnlySet<string> changedFields)
        => status == CompletenessStatus.Complete && changedFields.Count > 0;

    /// <summary>
    /// Computes the status a row should carry after an apply, given its status beforehand and the
    /// field names still listed as unknown afterward. Transitions <see cref="CompletenessStatus.Incomplete"/>
    /// to <see cref="CompletenessStatus.NeedsReview"/> whenever <paramref name="noValueKnownAfterApply"/>
    /// is empty — every field currently has a known value, whether that's because a later apply just
    /// filled in the last gap, or because the row was fully specified from the moment it was created;
    /// both are equally "nothing left flagged unknown, a human should confirm it's actually correct."
    /// Never changes an already-<see cref="CompletenessStatus.Complete"/> or already-
    /// <see cref="CompletenessStatus.NeedsReview"/> status — only a human (via an explicit decide-time
    /// override) or this automatic first-time transition ever move a row into either state.
    /// </summary>
    public static CompletenessStatus ComputeNextStatus(CompletenessStatus current, IReadOnlyList<string> noValueKnownAfterApply)
        => current == CompletenessStatus.Incomplete && noValueKnownAfterApply.Count == 0
            ? CompletenessStatus.NeedsReview
            : current;
}

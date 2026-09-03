namespace Quotinator.Data.Enums;

/// <summary>
/// What a matched <see cref="Quotinator.Data.Import.ConflictRuleLookup.TryResolve"/> call found when it
/// judged a rule's governed field against the current import (#374). Replaces the earlier single
/// <c>isStale</c> flag, which conflated "the rule's result is already in place" with "this rule can no
/// longer be applied."
/// </summary>
public enum ConflictRuleOutcome
{
    /// <summary>
    /// The incoming side still matches what the rule was authored against, and the stored value differs
    /// from what the rule wants (including when it is missing). Apply the rule: change it, or add it.
    /// </summary>
    Apply,

    /// <summary>
    /// The incoming side still matches what the rule was authored against, and the stored value already
    /// equals what the rule wants. Nothing to do — resolve to what is already stored.
    /// </summary>
    AlreadyApplied,

    /// <summary>
    /// The field is absent from the rule's recorded incoming snapshot, or the current incoming value no
    /// longer matches what was recorded — the underlying source moved since the rule was authored and
    /// the rule can no longer be trusted. Hold for review; the rule itself needs re-authoring.
    /// </summary>
    Stale,

    /// <summary>
    /// As <see cref="Stale"/>, but the incoming side has moved <em>to</em> agreement: the field would now
    /// resolve identically with the rule removed. Hold for review the same way, but the remedy is the
    /// opposite one — the rule is a candidate for deletion, not re-authoring.
    /// </summary>
    Retirable
}

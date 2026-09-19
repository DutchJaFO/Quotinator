namespace Quotinator.Data.Enums;

/// <summary>What happened to a decision staged on one import action.</summary>
public enum ImportActionDecideOutcome
{
    /// <summary>The decision was stored and the action is now <c>Decided</c>.</summary>
    Decided,

    /// <summary>No action exists with that id.</summary>
    NotFound,

    /// <summary>The action is already applied or discarded, so there is nothing left to decide.</summary>
    AlreadyResolved,

    /// <summary>The action's entity type and kind do not accept a manual decision.</summary>
    NotDecidable,

    /// <summary>One or more genuinely ambiguous fields were left without a decision; nothing was stored.</summary>
    UnresolvedFields,

    /// <summary>
    /// The action is held for review because of its incoming content (#410): it is resolved by correcting
    /// the imported file or adding a rule, not by a decision on the action.
    /// </summary>
    HeldForReview,
}

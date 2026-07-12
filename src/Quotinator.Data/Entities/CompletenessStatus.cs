namespace Quotinator.Data.Entities;

/// <summary>
/// Whether a content record's fields are known to be fully populated and reviewed. A closed set
/// this project's own <see cref="Import.CompletenessGuard"/> logic assigns and transitions between —
/// per ADR 008, backed by a matching SQL CHECK constraint wherever it's used as a column type.
/// </summary>
public enum CompletenessStatus
{
    /// <summary>Nothing known yet — the default for every newly created row.</summary>
    Incomplete,

    /// <summary>
    /// System-set only: every field that was listed as unknown now has a value, but no human has
    /// confirmed the record is actually correct and complete.
    /// </summary>
    NeedsReview,

    /// <summary>
    /// Human-set only, via an import action decision. A record in this state can never be silently
    /// modified by a later import — see <see cref="Import.CompletenessGuard.ShouldBlock"/>.
    /// </summary>
    Complete
}

namespace Quotinator.Data.Enums;

/// <summary>
/// How a <see cref="DatabaseAction"/> run from within the application ended (#348), as its result
/// notification records it.
/// </summary>
public enum DatabaseActionOutcome
{
    /// <summary>The action ran to completion. What it left to restore from is recorded beside it.</summary>
    Completed,

    /// <summary>The action did not run, and nothing was changed: an obstacle stopped it first.</summary>
    Refused,

    /// <summary>A Reset ran, but the rebuilt schema does not match the one its migration path defines (#305).</summary>
    NotIntact,
}

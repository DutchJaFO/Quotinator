namespace Quotinator.Data.Enums;

/// <summary>
/// What an operation that replaces data left behind to restore from (#348): the caller has to be told,
/// since a consumer that does not know there is no restore point cannot act on it.
/// </summary>
public enum RestorePointOutcome
{
    /// <summary>A backup was taken before the data was replaced.</summary>
    Succeeded,

    /// <summary>
    /// No backup was taken, because none could be and the caller explicitly accepted proceeding without
    /// one. There is no restore point.
    /// </summary>
    Skipped,

    /// <summary>
    /// The old database file could not be read, so it was never a backup: it was moved aside unchanged
    /// rather than copied (#482).
    /// </summary>
    SetAside,
}

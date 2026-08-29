namespace Quotinator.Data.Enums;

/// <summary>
/// What happened when a caller asked for a backup to be removed (#349).
/// <para>
/// A <see langword="bool"/> was not enough. It conflated "there was no such backup" with "the backup is
/// there and could not be removed", and left a filesystem failure to escape as an exception — which
/// reached the operator as an unhandled <c>500</c> on a read-only data directory, found during this
/// issue's own T2 pass. Those are three different answers with three different next actions, so they
/// are three different members.
/// </para>
/// </summary>
public enum BackupDeleteOutcome
{
    /// <summary>The backup was removed.</summary>
    Deleted,

    /// <summary>No backup of that name exists. The caller asked for something that was never there.</summary>
    NotFound,

    /// <summary>
    /// The name is not one this application will act on — a path, a traversal, or an artefact this
    /// application writes for its own purposes rather than a backup.
    /// </summary>
    InvalidName,

    /// <summary>
    /// The backup exists but could not be removed — typically a read-only data directory or a
    /// permission problem. Remedied outside the container, by restoring write access.
    /// </summary>
    NotRemovable,
}

namespace Quotinator.Data.Enums;

/// <summary>
/// What happened when a caller asked to read a backup's bytes (#349).
/// <para>
/// The read side needs the same four answers the delete side does, and for the same reason: a
/// <see langword="null"/> stream conflated "there is no such backup" with "the backup is there and
/// could not be opened", and an IO failure escaped as an exception — which reached the operator as an
/// unhandled <c>500</c> when a download hit a locked file, found in T1.
/// </para>
/// </summary>
public enum BackupReadOutcome
{
    /// <summary>The backup was opened; the caller owns the stream.</summary>
    Opened,

    /// <summary>No backup of that name exists.</summary>
    NotFound,

    /// <summary>
    /// The name is not one this application will act on — a path, a traversal, or an artefact this
    /// application writes for its own purposes rather than a backup.
    /// </summary>
    InvalidName,

    /// <summary>
    /// The backup exists but could not be opened — something holds it exclusively, or permissions
    /// refuse the read. Distinct from <see cref="NotFound"/> because the remedies differ entirely.
    /// </summary>
    NotReadable,
}

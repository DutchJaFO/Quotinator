namespace Quotinator.Data.Enums;

/// <summary>
/// What stopped a Reset that needed no backup (#482): the database file could not be read, so it is
/// set aside rather than backed up, and setting it aside and replacing it is what failed.
/// <para>
/// Separate from <see cref="BackupOutcome"/> because none of these is about a backup. Reporting one as a
/// backup obstacle would send an operator to fix the backup folder when the data directory is the fault.
/// Not persisted, so it carries no <c>CHECK</c> constraint (ADR 008).
/// </para>
/// </summary>
public enum ResetObstacle
{
    /// <summary>The data directory cannot be written, so the new database cannot be built or put in place.</summary>
    DataDirectoryNotWritable,

    /// <summary>The reserved folder for unreadable files cannot be created or written, so the file has nowhere to go.</summary>
    SetAsideFolderNotWritable,

    /// <summary>Moving the unreadable file into the reserved folder failed, for example because it is still open or the move needed space it did not have.</summary>
    SetAsideMoveFailed,
}

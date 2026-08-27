namespace Quotinator.Data.Enums;

/// <summary>
/// Why a database backup attempt did or did not produce a file (#327).
/// <para>
/// A backup exists to make a startup or a destructive admin action safe. When one cannot be taken, that
/// is a failure to report with options attached — not something to pass over silently — so every caller
/// needs to know <em>which</em> obstacle it hit, since the five have five different remedies.
/// </para>
/// <para>
/// Attribution is structural rather than message-parsing: each step of the backup is attempted on its
/// own, so the member is decided by which step failed. By the time the copy itself runs, the destination
/// has already been proven creatable and openable, so a failure there belongs to the source.
/// </para>
/// </summary>
public enum BackupOutcome
{
    /// <summary>A backup file was written.</summary>
    Succeeded,

    /// <summary>
    /// Writing this backup would push the backups folder past its configured size budget. Remedied by
    /// removing older backups, or by raising the budget.
    /// </summary>
    BudgetExceeded,

    /// <summary>
    /// The volume has less free space than the database's own size. Remedied by freeing space; removing
    /// older backups reclaims some of it.
    /// </summary>
    InsufficientDiskSpace,

    /// <summary>
    /// The backups folder itself could not be created — typically a read-only mount or a permission
    /// problem on the data directory. Remedied outside the container, by restoring write access.
    /// </summary>
    DestinationDirectoryNotWritable,

    /// <summary>
    /// The backups folder exists but the backup file within it could not be created or opened. Same
    /// class of remedy as <see cref="DestinationDirectoryNotWritable"/>, distinguished from it because
    /// a folder that exists and a folder that can be written to are different facts.
    /// </summary>
    DestinationFileNotWritable,

    /// <summary>
    /// The database being backed up could not be read — it is corrupt, truncated, or not a database at
    /// all. No backup of this file is possible by any means, which makes it the one variant whose
    /// remedy is not "fix the destination".
    /// </summary>
    SourceUnreadable,

    /// <summary>
    /// The volume ran out of space partway through the copy, after the pre-flight check had passed.
    /// Separated from <see cref="SourceUnreadable"/> because both fail at the same step and blaming a
    /// full disk on a healthy database would send an operator after the wrong fault entirely.
    /// </summary>
    DiskFilledDuringBackup,

    /// <summary>
    /// The attempt failed in a way this enum does not name. Deliberately present rather than folding an
    /// unrecognised failure into whichever member looks closest: per <c>docs/knowledgebase.md</c>'s
    /// triage table an unknown impact is an unanswered question, not "harmless by default", so the
    /// underlying error is carried through for an operator to report.
    /// </summary>
    Unclassified,
}

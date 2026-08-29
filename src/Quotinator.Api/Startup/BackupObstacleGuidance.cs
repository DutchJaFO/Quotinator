using Quotinator.Data.Enums;

namespace Quotinator.Api.Startup;

/// <summary>
/// Turns a <see cref="BackupOutcome"/> into what an operator is actually told (#348): what happened,
/// and what they can do about it.
/// <para>
/// Lives in the Api layer rather than in <c>Quotinator.Data</c>, which is domain-agnostic per ADR 004
/// and has no business deciding an operator's wording. That separation is also what keeps this text
/// localisable later without reaching into the data layer.
/// </para>
/// <para>
/// Every variant states symptom, cause and remedy. That is not presentation polish — per
/// <c>docs/knowledgebase.md</c>'s own sweep finding, a message nobody can write a Knowledgebase entry
/// for is a message that does not say enough, and #333 will derive entries from exactly these strings.
/// No <c>QTN-</c> code is allocated here: #333 requirement 5 puts the mechanical sweep before any code
/// allocation, and requirement 8 records that #326's data-directory reason shipped without one for the
/// same reason — there is nowhere to look one up yet.
/// </para>
/// </summary>
internal static class BackupObstacleGuidance
{
    /// <summary>What happened, in one sentence an operator can act on.</summary>
    /// <param name="obstacle">The obstacle that stopped the backup.</param>
    internal static string Cause(BackupOutcome obstacle) => obstacle switch
    {
        BackupOutcome.BudgetExceeded =>
            "The backup folder has reached its storage quota, so no new backup can be written.",
        BackupOutcome.InsufficientDiskSpace =>
            "The volume holding the backup folder has no free space left, so no new backup can be written.",
        BackupOutcome.DestinationDirectoryNotWritable =>
            "The backup folder could not be created — the data directory is read-only, or the container "
            + "user lacks write permission on it.",
        BackupOutcome.DestinationFileNotWritable =>
            "The backup folder exists but a backup file could not be created inside it, which usually "
            + "means a permission problem on that folder.",
        BackupOutcome.SourceUnreadable =>
            "The database file itself cannot be read — it is corrupt, truncated, or not a database. No "
            + "backup of it is possible by any means.",
        BackupOutcome.DiskFilledDuringBackup =>
            "The volume ran out of space partway through writing the backup, after the pre-flight check "
            + "had passed.",
        // Deliberately says it does not know, rather than guessing. An operator can act on "unknown,
        // here is the error"; a confident wrong cause sends them after a fault that is not there.
        _ => "The backup could not be taken, and the cause was not one this build recognises.",
    };

    /// <summary>
    /// What the operator can do, most actionable first. Empty only where there genuinely is nothing —
    /// which never happens today, and would itself be worth reporting if it did.
    /// </summary>
    /// <param name="obstacle">The obstacle that stopped the backup.</param>
    /// <param name="overrideAlreadyTried">
    /// Whether the caller already passed the override on this request. When they did and it still
    /// refused, offering it again would repeat advice that has just been shown not to work — found live
    /// on a read-only <c>/data</c>, where the override cannot help because the reset itself cannot write
    /// either. The remaining remedies are the ones that have not been disproved.
    /// </param>
    internal static IReadOnlyList<string> Remedies(BackupOutcome obstacle, bool overrideAlreadyTried = false)
    {
        IReadOnlyList<string> all = RemediesFor(obstacle);
        return overrideAlreadyTried
            ? [.. all.Where(r => !r.Contains("allowNoBackup", StringComparison.Ordinal))]
            : all;
    }

    private static IReadOnlyList<string> RemediesFor(BackupOutcome obstacle) => obstacle switch
    {
        BackupOutcome.BudgetExceeded =>
        [
            // #349 gave this remedy a route. Until those endpoints existed it described an action the
            // operator had no way to perform from inside the application, which is the gap that issue
            // was filed to close — so it names them now rather than leaving the advice abstract.
            "Remove one or more old backups to free quota — list them with GET /api/v1/admin/backups "
            + "and remove one with DELETE /api/v1/admin/backups/{name}.",
            "Raise the quota by increasing Quotinator:MaxBackupStorageGb, then restart.",
            "Retry with allowNoBackup=true to proceed without a backup, accepting that this action will "
            + "have no restore point.",
        ],
        BackupOutcome.InsufficientDiskSpace =>
        [
            "Free disk space on the volume holding the data directory.",
            "Removing old backups reclaims some of that space — DELETE /api/v1/admin/backups/{name}.",
            "Retry with allowNoBackup=true to proceed without a backup, accepting that this action will "
            + "have no restore point.",
        ],
        BackupOutcome.DestinationDirectoryNotWritable or BackupOutcome.DestinationFileNotWritable =>
        [
            "Restore write access to the data directory — remount the volume writable, or correct its "
            + "permissions — then restart.",
            "Retry with allowNoBackup=true to proceed without a backup, accepting that this action will "
            + "have no restore point.",
        ],
        // Removing old backups is deliberately absent here: the obstacle is the source, so freeing
        // destination space changes nothing. Naming a remedy that cannot work is the defect #326 fixed
        // for the data-directory case, and repeating it here would be the same mistake in a new place.
        // No allowNoBackup here, and that omission is measured rather than assumed: a database SQLite
        // will not open cannot be dropped table-by-table either, so a reset has nothing to work with
        // whatever the caller accepts. Offering the override would name a remedy that cannot succeed —
        // the same defect #326 fixed for the data-directory case. The file has to be replaced from
        // outside the application.
        BackupOutcome.SourceUnreadable =>
        [
            "Stop the application, move or delete the database file, and restart — the database will be "
            + "rebuilt empty.",
            "Restore an older backup in place of the unreadable file, then restart.",
        ],
        BackupOutcome.DiskFilledDuringBackup =>
        [
            "Free disk space on the volume holding the data directory, then retry.",
            "Remove the partially written backup file if one was left behind.",
        ],
        _ =>
        [
            "Check the application log for the underlying error, and report it if it is not obvious.",
            "Retry with allowNoBackup=true to proceed without a backup, accepting that this action will "
            + "have no restore point.",
        ],
    };
}

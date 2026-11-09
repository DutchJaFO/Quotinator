using Quotinator.Data.Database;
using Quotinator.Data.Enums;

namespace Quotinator.Api.Startup;

/// <summary>
/// Turns a <see cref="ResetObstacle"/> into what an operator is told (#482): what stopped a Reset of a
/// database file that could not be read, and what to do about it. The same cause-and-remedies shape as
/// <see cref="BackupObstacleGuidance"/>, kept apart because none of these is a backup problem, and the
/// endpoint and the repair action word them from here.
/// </summary>
internal static class ResetObstacleGuidance
{
    /// <summary>What happened, in one sentence an operator can act on.</summary>
    /// <param name="obstacle">What stopped the Reset.</param>
    internal static string Cause(ResetObstacle obstacle) => obstacle switch
    {
        ResetObstacle.DataDirectoryNotWritable =>
            "The database file cannot be read, and the data directory cannot be written, so the new "
            + "database can be neither built nor put in its place. Nothing was moved or changed.",
        ResetObstacle.SetAsideFolderNotWritable =>
            $"The database file cannot be read, and the \"{BackupFileNames.UnreadableFolderName}\" folder inside "
            + "the backups folder, where it is set aside, could not be created or written. Nothing was moved or changed.",
        ResetObstacle.SetAsideMoveFailed =>
            "The database file cannot be read, and moving it aside failed: it may still be open, or the "
            + "backups folder may be on another volume without room for it. Nothing was moved or changed.",
        _ => "The Reset was refused for a reason this build does not recognise. Nothing was moved or changed.",
    };

    /// <summary>What the operator can do, most actionable first.</summary>
    /// <param name="obstacle">What stopped the Reset.</param>
    internal static IReadOnlyList<string> Remedies(ResetObstacle obstacle) => obstacle switch
    {
        ResetObstacle.DataDirectoryNotWritable =>
        [
            "Restore write access to the data directory (remount the volume writable, or correct its "
            + "permissions), then retry the Reset.",
        ],
        ResetObstacle.SetAsideFolderNotWritable =>
        [
            "Restore write access to the backups folder, and remove anything occupying the "
            + $"\"{BackupFileNames.UnreadableFolderName}\" name inside it, then retry the Reset.",
        ],
        ResetObstacle.SetAsideMoveFailed =>
        [
            "Retry the Reset once nothing else has the database file open.",
            "If the backups folder is on another volume, free space there for a copy of the database file, then retry.",
        ],
        _ =>
        [
            "Check the application log for the underlying error, and report it if it is not obvious.",
        ],
    };
}

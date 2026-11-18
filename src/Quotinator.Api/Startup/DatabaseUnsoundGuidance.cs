using Quotinator.Constants.Api;
using Quotinator.Constants.Routes;
using Quotinator.Data.Database;
using Quotinator.Data.Database.Checks;
using Quotinator.Data.Enums;
using Quotinator.Data.Paths;

namespace Quotinator.Api.Startup;

/// <summary>
/// What an operator is told when the database check finds the main database file is not a sound copy, or cannot
/// open it at all (#457): what is wrong with the file, in engine-neutral words, and the remedies that work for it.
/// <para>
/// Before #457 every one of these took the generic initialisation-failure reason, which diagnosed a schema-version
/// mismatch: the wrong fault, with the wrong remedy first for a file that may be intact. Each layer now has a cause of
/// its own (decision B), and a file that could not be opened leads with restoring access (decision E). The Reset and
/// backup remedies are <see cref="BackupObstacleGuidance"/>'s, and the reserved folder's obstacle
/// <see cref="ResetObstacleGuidance"/>'s, so each sentence has one copy.
/// </para>
/// </summary>
internal static class DatabaseUnsoundGuidance
{
    /// <summary>
    /// Whether this reason describes <paramref name="failure"/>: a file layer that failed, the layers that mean the file
    /// is not a sound copy (ADR 028), or a file the Storage layer found but could not open.
    /// </summary>
    /// <param name="failure">The layer that failed.</param>
    internal static bool Covers(DatabaseCheckLayerResult failure) =>
        failure.Status == DatabaseCheckLayerStatus.Failed
        && (failure.Layer is DatabaseCheckLayer.Format or DatabaseCheckLayer.Journal or DatabaseCheckLayer.Pages or DatabaseCheckLayer.Indexes
            || failure is { Layer: DatabaseCheckLayer.Storage, StorageFault: DatabaseStorageFault.FileUnreadable });

    /// <summary>What is wrong with the file, one sentence per layer, shared with #491's report.</summary>
    /// <param name="failure">The layer that failed, one <see cref="Covers"/> accepts.</param>
    internal static string Cause(DatabaseCheckLayerResult failure) => failure.Layer switch
    {
        DatabaseCheckLayer.Storage =>
            "The database file exists but could not be opened: its permissions do not allow reading it, or another "
            + "process holds it locked. The file itself may be intact.",
        DatabaseCheckLayer.Format =>
            "The database file is not a database this application can open: it is not a database at all, or its "
            + "header is damaged.",
        DatabaseCheckLayer.Journal =>
            $"The write-ahead file beside the database ({DataPaths.DatabaseFile}-wal) is damaged or does not belong "
            + "to it, so the changes it holds cannot be trusted.",
        DatabaseCheckLayer.Pages =>
            "Parts of the database file are damaged or missing: it was cut short or overwritten, for example by a full "
            + "disk, an interrupted copy, or something other than this application writing to it.",
        DatabaseCheckLayer.Indexes =>
            "The database file opens, but an index no longer agrees with its table, or a unique constraint does not "
            + "hold, so what it returns cannot be trusted.",
        _ => $"The database check found the file unsound at its {failure.Layer} layer.",
    };

    /// <summary>The degraded reason for <paramref name="failure"/>: its cause, the remedies in order, and where the whole story is written down.</summary>
    /// <param name="failure">The layer that failed, one <see cref="Covers"/> accepts.</param>
    /// <param name="obstacle">What would stop a Reset setting the file aside, or <see langword="null"/>.</param>
    internal static string Reason(DatabaseCheckLayerResult failure, ResetObstacle? obstacle) =>
        $"{Cause(failure)} The application does not use it. "
        + string.Join(" ", Remedies(failure, obstacle))
        + $" See {KnowledgebaseLinks.TheDatabaseFileCannotBeRead}";

    /// <summary>
    /// Decision 2 for a file that is not a sound copy: the Reset, a backup, then deleting the file. Decision E for a
    /// file that could not be opened: access first, a backup, and the Reset last. Decision 3: with an obstacle, the
    /// Reset is replaced by what stops it.
    /// </summary>
    private static IReadOnlyList<string> Remedies(DatabaseCheckLayerResult failure, ResetObstacle? obstacle)
    {
        bool couldNotBeOpened = failure.Layer == DatabaseCheckLayer.Storage;

        string reset = obstacle is ResetObstacle o
            ? WhatStopsTheReset(o)
            : couldNotBeOpened
                ? BackupObstacleGuidance.ResetSetsTheFileAside + " The file may be intact, so do this only if access cannot be restored."
                : BackupObstacleGuidance.ResetSetsTheFileAside;

        return couldNotBeOpened
            ?
            [
                "Restore read access to the database file, or stop whatever holds it open, then restart.",
                BackupObstacleGuidance.RestoreAnOlderBackup,
                reset,
            ]
            :
            [
                reset,
                BackupObstacleGuidance.RestoreAnOlderBackup,
                "Or stop the application and restart it after deleting the database file, which creates a new, empty "
                + "database in its place.",
            ];
    }

    private static string WhatStopsTheReset(ResetObstacle obstacle) => obstacle switch
    {
        ResetObstacle.SetAsideFolderNotWritable =>
            $"A Reset cannot set the file aside now: the \"{BackupFileNames.UnreadableFolderName}\" folder inside the "
            + $"backups folder could not be created or written. {ResetObstacleGuidance.FreeTheSetAsideFolder}, and a "
            + $"Reset (POST {ApiRoutes.DatabaseReset}) will then set it aside.",
        _ => $"A Reset cannot proceed now. {ResetObstacleGuidance.Cause(obstacle)} "
            + string.Join(" ", ResetObstacleGuidance.Remedies(obstacle)),
    };
}

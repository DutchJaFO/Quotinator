using Quotinator.Data.Enums;

namespace Quotinator.Data.Database.Checks;

/// <summary>
/// Which way a database check sends a Reset (#490, ADR 028's <i>Which way a failure sends a Reset</i>).
/// </summary>
internal static class ResetDatabaseCheck
{
    /// <summary>The layers that judge whether the file itself is sound: its storage, format, journal, pages and indexes.</summary>
    private static readonly DatabaseCheckLayer[] FileLayers =
    [
        DatabaseCheckLayer.Format, DatabaseCheckLayer.Journal, DatabaseCheckLayer.Pages, DatabaseCheckLayer.Indexes,
    ];

    /// <summary>
    /// Whether the file is a sound copy, so a Reset backs it up (ADR 025) rather than setting it aside. It is
    /// when every layer from Format to Indexes passed. One that failed, was inconclusive, or did not run because
    /// Storage could not read the file or make the copy they needed means it is not: a file nobody could show
    /// is sound is never kept as a backup. A References or Schema failure leaves it a sound copy, and a directory that cannot be written is
    /// the backup's own obstacle, found by the backup's readiness check. A file that does not exist passes every
    /// layer, and a Reset creates it.
    /// </summary>
    /// <param name="check">The check the Reset ran, at <see cref="DatabaseCheckDepth.Full"/>.</param>
    internal static bool IsSoundCopy(DatabaseCheckResult check) =>
        check.Layers.Where(l => FileLayers.Contains(l.Layer)).All(l => l.Status == DatabaseCheckLayerStatus.Passed);

    /// <summary>
    /// The layer that shows the file is not a sound copy, or <see langword="null"/> when it is one. When the file
    /// layers did not run, it is the failure that stopped them, Storage's, since that is what was found.
    /// </summary>
    /// <param name="check">The check the Reset ran.</param>
    internal static DatabaseCheckLayerResult? WhyNotASoundCopy(DatabaseCheckResult check)
    {
        DatabaseCheckLayerResult? layer = check.Layers.FirstOrDefault(l => FileLayers.Contains(l.Layer) && l.Status != DatabaseCheckLayerStatus.Passed);
        return layer is { Status: DatabaseCheckLayerStatus.NotRun } ? check.FirstFailure ?? layer : layer;
    }

    /// <summary>
    /// The Storage result that found too little room for the check's copy, or <see langword="null"/>. Room for
    /// that copy is room a backup needs too (developer, 2026-11-15), so a Reset refuses as for any backup obstacle.
    /// </summary>
    /// <param name="check">The check the Reset ran.</param>
    internal static DatabaseCheckLayerResult? NoRoomToCheck(DatabaseCheckResult check) =>
        check.Layers.FirstOrDefault(l => l.Layer == DatabaseCheckLayer.Storage && l.StorageFault == DatabaseStorageFault.InsufficientSpace);
}

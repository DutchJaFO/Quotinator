using Quotinator.Data.Enums;

namespace Quotinator.Api.Services;

/// <summary>
/// The volatile state a notification's action depends on, read once per render (#369).
/// </summary>
/// <remarks>
/// A notification outlives the records it names (its metadata is written to survive exactly that), so
/// whether its action can still be carried out is a fact about the present, not about the row. This is
/// gathered once by <see cref="INotificationActionExecutor.GetAvailabilityAsync"/> and handed to every
/// row, so no row runs a query of its own.
/// </remarks>
/// <param name="liveImportBatchIds">The id of every import batch that still exists.</param>
/// <param name="backupReadiness">Whether a backup can be taken now, and if not, what is in the way (#348).</param>
/// <param name="backupReadinessWithOldestRemoved">
/// What the same check would answer once the oldest backup is removed, or <see langword="null"/> when
/// there is no backup to remove (#348).
/// </param>
/// <param name="backupCaution">
/// Whether the backups folder is already at the operating quota, so any backup taken now runs inside the
/// reserve and may reach the ceiling while it is taken (#348).
/// </param>
public sealed class NotificationActionAvailability(
    IEnumerable<string> liveImportBatchIds,
    BackupOutcome backupReadiness = BackupOutcome.Succeeded,
    BackupOutcome? backupReadinessWithOldestRemoved = null,
    bool backupCaution = false)
{
    // Case-insensitive per ADR 012: a payload's batch id and a batch row's id are two independently
    // cased copies of the same value.
    private readonly HashSet<string> _liveImportBatchIds = new(liveImportBatchIds, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the import batch <paramref name="batchId"/> still exists.</summary>
    /// <param name="batchId">The batch id a notification's payload names.</param>
    public bool ImportBatchExists(string batchId) => _liveImportBatchIds.Contains(batchId);

    /// <summary>Whether a backup can be taken now, and if not, what is in the way.</summary>
    public BackupOutcome BackupReadiness { get; } = backupReadiness;

    /// <summary>What the backup check would answer once the oldest backup is removed; <see langword="null"/> when none exists.</summary>
    public BackupOutcome? BackupReadinessWithOldestRemoved { get; } = backupReadinessWithOldestRemoved;

    /// <summary>
    /// Whether an option that takes a backup is offered with a caution: the folder is at the quota, so the
    /// backup runs inside the reserve and may reach the ceiling while it is taken.
    /// </summary>
    public bool BackupCaution { get; } = backupCaution;
}

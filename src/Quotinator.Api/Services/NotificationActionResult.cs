using Quotinator.Data.Enums;

namespace Quotinator.Api.Services;

/// <summary>
/// What running a notification's action did (#348). A refusal is an outcome, not an exception: the
/// action stopped before changing anything because a backup could not be taken, and the notification
/// stays active so the user can choose again.
/// </summary>
public sealed class NotificationActionResult
{
    /// <summary>Whether the action ran to completion.</summary>
    public required bool Succeeded { get; init; }

    /// <summary>What stopped the backup the action needed, when that is why it did not run.</summary>
    public BackupOutcome? BackupObstacle { get; init; }

    /// <summary>An action that ran.</summary>
    public static NotificationActionResult Success() => new() { Succeeded = true };

    /// <summary>An action that did not run because no backup could be taken first.</summary>
    /// <param name="obstacle">What stopped the backup.</param>
    public static NotificationActionResult RefusedForBackup(BackupOutcome obstacle) =>
        new() { Succeeded = false, BackupObstacle = obstacle };
}

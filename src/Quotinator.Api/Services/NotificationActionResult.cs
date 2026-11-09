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

    /// <summary>
    /// Why the action left the application degraded when it was not a backup obstacle: a Reset that ran
    /// but whose rebuilt schema does not match its migration path (#305).
    /// </summary>
    public string? FailureReason { get; init; }

    /// <summary>
    /// What stopped a Reset of an unreadable database file, when that is why it did not run (#482). Such a
    /// Reset needs no backup, so <see cref="BackupObstacle"/> is never set alongside it.
    /// </summary>
    public ResetObstacle? ResetObstacle { get; init; }

    /// <summary>An action that ran.</summary>
    public static NotificationActionResult Success() => new() { Succeeded = true };

    /// <summary>An action that did not run because no backup could be taken first.</summary>
    /// <param name="obstacle">What stopped the backup.</param>
    public static NotificationActionResult RefusedForBackup(BackupOutcome obstacle) =>
        new() { Succeeded = false, BackupObstacle = obstacle };

    /// <summary>A Reset that ran, but whose rebuilt schema does not match its migration path (#305).</summary>
    /// <param name="reason">The degraded reason, naming every missing or different object and the remedies.</param>
    public static NotificationActionResult ResetNotIntact(string reason) =>
        new() { Succeeded = false, FailureReason = reason };

    /// <summary>A Reset of an unreadable database file that did not run: the file could not be set aside (#482).</summary>
    /// <param name="obstacle">What stopped it.</param>
    public static NotificationActionResult RefusedForReset(ResetObstacle obstacle) =>
        new() { Succeeded = false, ResetObstacle = obstacle };
}

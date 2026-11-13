using Quotinator.Data.Enums;

namespace Quotinator.Api.Services;

/// <summary>
/// Records the end result of a Reset or a reseed run from within the application as a notification
/// (#348): what was done, whether a backup was taken or skipped, or what refused it. A notification is
/// the lasting record of what the user was told, so they can see later that it happened.
/// </summary>
public interface IDatabaseActionResultRecorder
{
    /// <summary>
    /// Writes the notification for one run's result. Never throws: the action's own result stands whether
    /// or not its record could be written, and a failure to write it is logged.
    /// </summary>
    /// <param name="action">Which action ran.</param>
    /// <param name="result">How it ended.</param>
    Task RecordAsync(DatabaseAction action, NotificationActionResult result);
}

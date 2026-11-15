using Microsoft.Extensions.Logging;
using Quotinator.Data.Database;
using Quotinator.Data.Enums;
using Quotinator.Data.Notifications;
using Quotinator.Data.Repositories;
using Quotinator.Logging;

namespace Quotinator.Api.Services;

/// <inheritdoc cref="IDatabaseActionResultRecorder"/>
/// <param name="notificationReader">Supplies the history the write's identity is compared against.</param>
/// <param name="notificationWriter">Writes the notification.</param>
/// <param name="textSource">Resolves its title and body in every language.</param>
/// <param name="logger">Logs a record that could not be written.</param>
internal sealed class DatabaseActionResultRecorder(
    INotificationReader notificationReader, INotificationWriter notificationWriter,
    INotificationTextSource textSource, ILogger<DatabaseActionResultRecorder> logger) : IDatabaseActionResultRecorder
{
    /// <inheritdoc/>
    public async Task RecordAsync(DatabaseAction action, NotificationActionResult result)
    {
        DatabaseActionResultMetadataDto metadata = Payload(action, result);
        (NotificationType type, string titleKey, string bodyKey, object[] bodyArgs) = Text(metadata);

        try
        {
            await NotificationSeeding.SeedOnceAsync(
                notificationReader, notificationWriter, type, metadata,
                body: NotificationTranslations.Original(textSource, bodyKey, bodyArgs),
                // Left unstated: a Reset empties the version row, which is recorded again only afterwards,
                // and the result is about this run rather than about a release.
                appVersionId: null,
                title: NotificationTranslations.Original(textSource, titleKey),
                dismissTrigger: null,
                translations: NotificationTranslations.Build(textSource, titleKey, bodyKey, bodyArgs: bodyArgs));
        }
        // The action has already ended and its result is already the caller's: a database that cannot take
        // the record must not turn a completed Reset into a failed one. The page has told the user directly.
        catch (Exception ex)
        {
            logger.LogExceptionHandled(ex, $"the result of a {action} could not be recorded as a notification");
        }
    }

    /// <summary>What the record carries: the facts of how this run ended, identified by the run.</summary>
    private static DatabaseActionResultMetadataDto Payload(DatabaseAction action, NotificationActionResult result)
    {
        // #490: a Reset that ran carries the reason on its restore point; a refused one, on the result.
        SetAsideReasonResult? reason = result.RestorePoint?.SetAsideReason ?? result.SetAsideReason;
        return new()
        {
            RunId          = Guid.NewGuid(),
            Action         = action,
            Outcome        = result switch
            {
                { Succeeded: true }            => DatabaseActionOutcome.Completed,
                { FailureReason: not null }    => DatabaseActionOutcome.NotIntact,
                _                              => DatabaseActionOutcome.Refused,
            },
            RestorePoint   = result.RestorePoint?.Outcome,
            FileName       = result.RestorePoint?.FileName,
            BackupObstacle = result.RestorePoint?.Obstacle ?? result.BackupObstacle,
            ResetObstacle  = result.ResetObstacle,
            CheckLayer     = reason?.CheckLayer?.Layer,
            CheckStatus    = reason?.CheckLayer?.Status,
            CheckFinding   = reason?.CheckLayer?.Finding,
            BackupError    = reason?.BackupError,
            ReleaseState   = NotificationReleaseState.NotApplicable,
        };
    }

    /// <summary>
    /// The type, title, body and body arguments for how the run ended. A backup taken is a success; one
    /// skipped, or a file set aside rather than backed up, is a warning; a refusal or a schema that still
    /// does not match is an error (developer decisions, 2026-11-12).
    /// </summary>
    private static (NotificationType Type, string TitleKey, string BodyKey, object[] BodyArgs) Text(DatabaseActionResultMetadataDto result)
    {
        bool reset = result.Action is DatabaseAction.Reset;

        return result switch
        {
            { Outcome: DatabaseActionOutcome.NotIntact } =>
                (NotificationType.Error, NotificationMessageKeys.DatabaseResetNotIntactTitle,
                 NotificationMessageKeys.DatabaseResetNotIntactBody, []),

            // #490: a refusal for a file that had to be set aside says why it had to be, as well as the obstacle.
            { Outcome: DatabaseActionOutcome.Refused, ResetObstacle: ResetObstacle resetObstacle, BackupError: string backupError } =>
                (NotificationType.Error, NotificationMessageKeys.DatabaseResetRefusedTitle,
                 NotificationMessageKeys.DatabaseResetRefusedForResetByBackupBody, [resetObstacle.ToString(), backupError]),

            { Outcome: DatabaseActionOutcome.Refused, ResetObstacle: ResetObstacle resetObstacle, CheckLayer: DatabaseCheckLayer layer } =>
                (NotificationType.Error, NotificationMessageKeys.DatabaseResetRefusedTitle,
                 NotificationMessageKeys.DatabaseResetRefusedForResetNotSoundBody,
                 [resetObstacle.ToString(), layer.ToString(), result.CheckStatus?.ToString() ?? string.Empty, result.CheckFinding ?? string.Empty]),

            { Outcome: DatabaseActionOutcome.Refused, ResetObstacle: ResetObstacle resetObstacle } =>
                (NotificationType.Error, NotificationMessageKeys.DatabaseResetRefusedTitle,
                 NotificationMessageKeys.DatabaseResetRefusedForResetBody, [resetObstacle.ToString()]),

            { Outcome: DatabaseActionOutcome.Refused } =>
                (NotificationType.Error,
                 reset ? NotificationMessageKeys.DatabaseResetRefusedTitle : NotificationMessageKeys.DatabaseReseedRefusedTitle,
                 NotificationMessageKeys.DatabaseActionRefusedForBackupBody,
                 [(result.BackupObstacle ?? BackupOutcome.Unclassified).ToString()]),

            { RestorePoint: RestorePointOutcome.Skipped } =>
                (NotificationType.Warning,
                 reset ? NotificationMessageKeys.DatabaseResetWithoutBackupTitle : NotificationMessageKeys.DatabaseReseedWithoutBackupTitle,
                 reset ? NotificationMessageKeys.DatabaseResetWithoutBackupBody : NotificationMessageKeys.DatabaseReseedWithoutBackupBody,
                 [(result.BackupObstacle ?? BackupOutcome.Unclassified).ToString()]),

            // #490: the reason it was set aside is the backup's own error when the check passed, and the
            // check layer that showed it otherwise.
            { RestorePoint: RestorePointOutcome.SetAside, BackupError: string backupError } =>
                (NotificationType.Warning, NotificationMessageKeys.DatabaseResetSetAsideTitle,
                 NotificationMessageKeys.DatabaseResetSetAsideByBackupBody, [result.FileName ?? string.Empty, backupError]),

            { RestorePoint: RestorePointOutcome.SetAside } =>
                (NotificationType.Warning, NotificationMessageKeys.DatabaseResetSetAsideTitle,
                 NotificationMessageKeys.DatabaseResetSetAsideBody,
                 [result.FileName ?? string.Empty, result.CheckLayer?.ToString() ?? string.Empty,
                  result.CheckStatus?.ToString() ?? string.Empty, result.CheckFinding ?? string.Empty]),

            _ =>
                (NotificationType.Success,
                 reset ? NotificationMessageKeys.DatabaseResetCompletedTitle : NotificationMessageKeys.DatabaseReseedCompletedTitle,
                 NotificationMessageKeys.DatabaseActionBackupTakenBody, [result.FileName ?? string.Empty]),
        };
    }
}

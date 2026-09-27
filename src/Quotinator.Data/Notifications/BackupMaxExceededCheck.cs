using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Helpers;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Notifications;

/// <summary>
/// Raises the backup maximum error while the backups folder is at or above its maximum, and removes it
/// once it is back under (#348, developer 2026-09-27).
/// <para>
/// Exceeding the maximum always reports, whichever way the folder arrived there: an attempt refused, a
/// backup that exceeded the estimate it was permitted on, or files written from outside the application.
/// The maximum is this application's own rather than the hardware's, so nothing physical stops a write
/// the way a full volume would, and a folder can pass it with no attempt having failed.
/// </para>
/// <para>
/// Its band and <see cref="BackupQuotaCheck"/>'s cannot overlap: that warning is valid only below the
/// maximum, this error only at or above it, so a folder is never described by both at once.
/// </para>
/// </summary>
/// <param name="options">The database options carrying the backups folder and the maximum.</param>
/// <param name="reader">Reads existing notifications, so the error is raised once while unresolved.</param>
/// <param name="writer">Raises and resolves the error.</param>
/// <param name="textSource">Supplies the error's title and body in every language.</param>
public sealed class BackupMaxExceededCheck(
    DatabaseOptions options,
    INotificationReader reader,
    INotificationWriter writer,
    INotificationTextSource textSource) : INotificationConditionCheck
{
    /// <inheritdoc/>
    public NotificationMetadataKind Kind => NotificationMetadataKind.BackupMaxExceeded;

    /// <inheritdoc/>
    public async Task<NotificationConditionOutcome> CheckAsync()
    {
        long used    = BackupStorageBudget.UsedBytes(options.BackupsPath);
        long ceiling = BackupStorageBudget.CeilingBytes(options);

        if (used < ceiling)
        {
            int resolved = await writer.DismissByTriggerAsync(NotificationDismissTrigger.BackupBackUnderMax, NotificationResolution.UnderMax);
            return resolved > 0 ? NotificationConditionOutcome.Cleared : NotificationConditionOutcome.Unchanged;
        }

        object[] bodyArgs = [ByteSize.Format(used), ByteSize.Format(ceiling)];

        NotificationEntity? raised = await NotificationSeeding.SeedWhileUnresolvedAsync(
            reader, writer, NotificationType.Error,
            new BackupMaxExceededMetadataDto
            {
                UsedBytes    = used,
                CeilingBytes = ceiling,
                ReleaseState = NotificationReleaseState.NotApplicable,
            },
            body: NotificationTranslations.Original(textSource, NotificationMessageKeys.BackupMaxExceededBody, bodyArgs),
            appVersionId: null,
            title: NotificationTranslations.Original(textSource, NotificationMessageKeys.BackupMaxExceededTitle),
            dismissTrigger: NotificationDismissTrigger.BackupBackUnderMax,
            translations: NotificationTranslations.Build(
                textSource,
                NotificationMessageKeys.BackupMaxExceededTitle,
                NotificationMessageKeys.BackupMaxExceededBody,
                bodyArgs: bodyArgs));

        return raised is null ? NotificationConditionOutcome.Unchanged : NotificationConditionOutcome.Raised;
    }
}

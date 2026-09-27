using Microsoft.Extensions.Logging;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Helpers;
using Quotinator.Data.Logging;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Notifications;

/// <summary>
/// Raises the backup quota warning while the backups folder is at or past its operating quota, and resolves
/// it once the folder is back under (#348, developer 2026-09-26).
/// <para>
/// Past the quota a backup is still taken, inside the reserve below the ceiling; this warning is how the user
/// learns the folder is there, so they can delete older backups or raise the quota before the ceiling
/// refuses one. "At or past" is the backup status reader's own <c>ReserveInUse</c>, so the warning and the
/// published status cannot disagree about where the folder stands.
/// </para>
/// </summary>
/// <param name="options">The database options carrying the backups folder, the budget and the quota.</param>
/// <param name="reader">Reads existing notifications, so the warning is raised once while unresolved.</param>
/// <param name="writer">Raises and resolves the warning.</param>
/// <param name="textSource">Supplies the warning's title and body in every language.</param>
/// <param name="logger">Reports a configured quota percentage that was ignored.</param>
public sealed class BackupQuotaCheck(
    DatabaseOptions options,
    INotificationReader reader,
    INotificationWriter writer,
    INotificationTextSource textSource,
    ILogger<BackupQuotaCheck> logger) : INotificationConditionCheck
{
    /// <inheritdoc/>
    public NotificationMetadataKind Kind => NotificationMetadataKind.BackupQuotaReached;

    /// <inheritdoc/>
    public async Task<NotificationConditionOutcome> CheckAsync()
    {
        // Reported here because this is where the quota takes effect: an out-of-range setting is ignored
        // loudly and the default used, never clamped silently and never fatal.
        BackupStorageBudget.EffectiveQuotaPercent(options, out bool outOfRange);
        if (outOfRange)
            logger.LogBackupQuotaPercentOutOfRange(options.BackupQuotaPercent, DatabaseOptions.DefaultBackupQuotaPercent);

        long used    = BackupStorageBudget.UsedBytes(options.BackupsPath);
        long quota   = BackupStorageBudget.QuotaBytes(options);
        long ceiling = BackupStorageBudget.CeilingBytes(options);

        if (used < quota)
        {
            int resolved = await writer.DismissByTriggerAsync(NotificationDismissTrigger.BackupQuotaRestored, NotificationResolution.UnderQuota);
            return resolved > 0 ? NotificationConditionOutcome.Cleared : NotificationConditionOutcome.Unchanged;
        }

        object[] bodyArgs = [ByteSize.Format(used), ByteSize.Format(quota), ByteSize.Format(ceiling)];

        NotificationEntity? raised = await NotificationSeeding.SeedWhileUnresolvedAsync(
            reader, writer, NotificationType.Warning,
            new BackupQuotaReachedMetadataDto
            {
                UsedBytes = used,
                QuotaBytes = quota,
                CeilingBytes = ceiling,
                ReleaseState = NotificationReleaseState.NotApplicable,
            },
            body: NotificationTranslations.Original(textSource, NotificationMessageKeys.BackupQuotaReachedBody, bodyArgs),
            appVersionId: null,
            title: NotificationTranslations.Original(textSource, NotificationMessageKeys.BackupQuotaReachedTitle),
            dismissTrigger: NotificationDismissTrigger.BackupQuotaRestored,
            translations: NotificationTranslations.Build(
                textSource,
                NotificationMessageKeys.BackupQuotaReachedTitle,
                NotificationMessageKeys.BackupQuotaReachedBody,
                bodyArgs: bodyArgs));

        return raised is null ? NotificationConditionOutcome.Unchanged : NotificationConditionOutcome.Raised;
    }
}

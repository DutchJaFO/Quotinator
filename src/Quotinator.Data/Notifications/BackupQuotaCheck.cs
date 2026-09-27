using Microsoft.Extensions.Logging;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Helpers;
using Quotinator.Data.Logging;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Notifications;

/// <summary>
/// Raises the backup quota warning while the backups folder is inside the reserve, and removes it once it
/// is not (#348, developer 2026-09-26).
/// <para>
/// Inside the reserve a backup is still taken; this warning is how the user learns the folder is there, so
/// they can delete older backups or raise the quota before the ceiling refuses one. The reserve is the band
/// between the quota and the ceiling, and the warning is valid only within it: below the quota there is
/// nothing to warn about, and at or above the ceiling backups are refused rather than taken, which is what
/// the error from that refused attempt says instead.
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

        // The band this warning describes, not merely the threshold it starts at (developer, 2026-09-27).
        // It says backups are still being taken from the reserve, which is false once the folder is at the
        // max and every backup is refused, so above the max the warning is no longer valid and goes the
        // same way as one below the quota. What applies there is the error from the refused attempt: a
        // warning claiming the reserve and an error claiming the max cannot both stand from one action.
        if (used < quota || used >= ceiling)
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

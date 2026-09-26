using Microsoft.Extensions.Logging;
using Quotinator.Api.Enums;
using Quotinator.Api.Logging;
using Quotinator.Api.Startup;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Helpers;
using Quotinator.Data.Models;
using Quotinator.Data.Notifications;
using Quotinator.Data.Repositories;

namespace Quotinator.Api.Services;

/// <inheritdoc cref="INotificationActionExecutor"/>
/// <remarks>Initialises the executor with every dependency an executable trigger might need.</remarks>
/// <param name="databaseInitializer">Runs the Reset action for <see cref="NotificationDismissTrigger.DatabaseReset"/>.</param>
/// <param name="databaseHealth">Marked healthy after a successful Reset, matching <c>AdminEndpoints.cs</c>'s own <c>POST /admin/database/reset</c> handler.</param>
/// <param name="notificationWriter">Dismisses any notification carrying the trigger just executed, matching <c>AdminEndpoints.cs</c>'s own reset-success wiring (#278 Step 6).</param>
/// <param name="appVersionTracker">Re-populates <c>System_AppVersion</c> after a Reset, matching <c>AdminEndpoints.cs</c>'s own reset-success wiring (#81).</param>
/// <param name="versionService">Supplies the current version for <paramref name="appVersionTracker"/>.</param>
/// <param name="logger">Logs a non-fatal warning if <paramref name="appVersionTracker"/>'s write fails.</param>
/// <param name="importActions">Resolves a staged batch for <see cref="NotificationDismissTrigger.ImportReviewResolved"/> (#303).</param>
/// <param name="importBatches">Supplies the batches that still exist, for <see cref="GetAvailabilityAsync"/> (#369).</param>
/// <param name="backupReader">Supplies the oldest backup, whose removal <see cref="GetAvailabilityAsync"/> weighs (#348).</param>
/// <param name="backupOperations">Takes and removes backups for a reseed, logged and audited as an operator's own request is (#348).</param>
/// <param name="auditWriter">Records a reseed that ran without a backup (#348).</param>
/// <param name="callerContext">Names who asked, for that audit entry.</param>
internal sealed class NotificationActionExecutor(
    IDatabaseInitializer databaseInitializer, DatabaseHealthState databaseHealth, INotificationWriter notificationWriter,
    IAppVersionTracker appVersionTracker, IVersionService versionService, ILogger<NotificationActionExecutor> logger,
        IImportActionService importActions, IImportBatchRepository importBatches, IDatabaseBackupReader backupReader,
    BackupOperations backupOperations, IAuditEntryWriter auditWriter, ICallerContext callerContext) : INotificationActionExecutor
{
    /// <inheritdoc/>
    public bool CanExecute(NotificationDismissTrigger trigger) => trigger switch
    {
        NotificationDismissTrigger.DatabaseReset => true,
        NotificationDismissTrigger.Reseed         => true,
        NotificationDismissTrigger.ImportReviewResolved => true,
        _                                         => false,
    };

    /// <inheritdoc/>
    public bool CanExecute(NotificationDismissTrigger trigger, NotificationMetadataDto? metadata, NotificationActionAvailability availability) =>
        AvailableOptions(trigger, metadata, availability).Count > 0;

    /// <inheritdoc/>
    public IReadOnlyList<NotificationActionOption> AvailableOptions(NotificationDismissTrigger trigger, NotificationMetadataDto? metadata, NotificationActionAvailability availability) => trigger switch
    {
        // #348: a Reset refuses without a backup, so offering it then would be offering a refusal.
        NotificationDismissTrigger.DatabaseReset =>
            availability.BackupReadiness == BackupOutcome.Succeeded ? [NotificationActionOption.ResetDatabase] : [],
        NotificationDismissTrigger.Reseed => ReseedOptions(availability),
        // #369: the batch is what this action decides and applies. Once it is gone there is nothing to
        // apply against, and without the payload there is no batch to name at all: the same condition
        // ExecuteAsync throws on below.
        NotificationDismissTrigger.ImportReviewResolved =>
            metadata is ImportReviewPendingMetadataDto review && availability.ImportBatchExists(review.BatchId)
                ? [NotificationActionOption.KeepExisting, NotificationActionOption.TakeIncoming]
                : [],
        _ => [],
    };

    /// <summary>
    /// The obstacles a reseed can still complete despite (#348): exactly the ones the pre-flight check
    /// reports. An unreadable source, a disk that filled mid-copy, and an unrecognised failure are only
    /// ever reported by an attempt, and a reseed is not offered without a backup for any of them.
    /// </summary>
    private static readonly HashSet<BackupOutcome> ObstaclesAReseedCanRunDespite =
    [
        BackupOutcome.BudgetExceeded, BackupOutcome.InsufficientDiskSpace,
        BackupOutcome.DestinationDirectoryNotWritable, BackupOutcome.DestinationFileNotWritable,
    ];

    // #348: with a backup possible, backing up first is the only option: removing one or going without
    // would each give up a restore point for nothing.
    private static List<NotificationActionOption> ReseedOptions(NotificationActionAvailability availability)
    {
        if (availability.BackupReadiness == BackupOutcome.Succeeded)
            return [NotificationActionOption.BackUpThenReseed];

        List<NotificationActionOption> options = [];

        if (availability.BackupReadinessWithOldestRemoved == BackupOutcome.Succeeded)
            options.Add(NotificationActionOption.RemoveOldestBackupThenReseed);

        if (ObstaclesAReseedCanRunDespite.Contains(availability.BackupReadiness))
            options.Add(NotificationActionOption.ReseedWithoutBackup);

        return options;
    }

    /// <inheritdoc/>
    public async Task<NotificationActionAvailability> GetAvailabilityAsync()
    {
        IReadOnlyList<ImportBatchEntity> batches = await importBatches.GetAllAsync();

        BackupOutcome readiness = databaseInitializer.CheckBackupReadiness();

        // Newest first, so the oldest is last. Weighed only by the check's own comparisons, never by a
        // second estimate of them, so offering its removal cannot drift from what the backup will find.
        IReadOnlyList<BackupFileInfo> backups = backupReader.List();
        BackupFileInfo? oldest = backups.Count == 0 ? null : backups[^1];
        BackupOutcome? withOldestRemoved = oldest is null
            ? null
            : databaseInitializer.CheckBackupReadiness(bytesFreedFirst: oldest.SizeBytes);

        // The same figure the backup status endpoint publishes, so the caution and the status cannot
        // disagree about whether the folder is at the quota.
        bool caution = backupReader.GetUsage().ReserveInUse;

        return new NotificationActionAvailability(batches.Select(batch => batch.Id.ToCanonicalId()), readiness, withOldestRemoved, caution);
    }

    /// <inheritdoc/>
    public async Task<NotificationActionResult> ExecuteAsync(
        NotificationDismissTrigger trigger, NotificationMetadataDto? metadata = null,
        FieldResolutionChoice? choice = null, NotificationActionOption? option = null)
    {
        switch (trigger)
        {
            // DatabaseReset takes no parameters from the notification: a schema-version overshoot is
            // resolved by truing up the whole database's version bookkeeping, so there is nothing for
            // the payload to narrow. metadata is accepted and ignored here rather than absent from the
            // contract, so #304's Reseed (which genuinely needs "reseed *this* file") is a new case
            // in this switch instead of an interface change rippling through every caller.
            case NotificationDismissTrigger.DatabaseReset:
            {
                // #348: a refused Reset changed nothing, so the database is exactly as broken as before and
                // the notification still describes it. Found 2026-09-26: this result used to be ignored,
                // marking the database healthy and dismissing the notification anyway.
                DatabaseOperationResult reset = await databaseInitializer.ResetAsync();
                if (!reset.Succeeded)
                    return NotificationActionResult.RefusedForBackup(reset.BackupObstacle ?? BackupOutcome.Unclassified);

                databaseHealth.MarkHealthy();
                await notificationWriter.DismissByTriggerAsync(
                    NotificationDismissTrigger.DatabaseReset, NotificationResolution.Reset);
                // #81: matches AdminEndpoints.cs's own POST /admin/database/reset wiring: Reset
                // rebuilds System_AppVersion empty like every other table, so re-populate it
                // immediately rather than leaving it empty until the next full app restart.
                // Non-fatal, same reasoning as AdminEndpoints.cs's own try/catch around this call.
                try
                {
                    await appVersionTracker.RecordCurrentAsync(versionService.Application, versionService.Version);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[Server] Failed to record the current app version after Reset; non-fatal, the reset itself still succeeded.");
                }
                break;
            }
            // #304. Deliberately not a copy of the case above: a reseed replaces content within an
            // intact schema, so it neither degrades health nor empties System_AppVersion, and calling
            // MarkHealthy or RecordCurrentAsync here would assert a recovery that never happened.
            //
            // metadata carries which files changed (ReseedRecommendedMetadataDto), and is ignored for
            // now: IDatabaseInitializer.ReseedAsync has no per-file overload, so there is nothing to
            // narrow to. The payload reaching this far is what makes adding one later a change to this
            // case rather than to the contract.
            case NotificationDismissTrigger.Reseed:
            {
                // #348: ReseedAsync never backs up on its own, since a function does one thing. This
                // caller offered the user the reseed, so it composes the steps: back up, then reseed.
                NotificationActionResult prepared = await PrepareReseedAsync(option ?? NotificationActionOption.BackUpThenReseed);
                if (!prepared.Succeeded)
                    return prepared;

                // Default forceSourceRefresh: the content that prompted the recommendation is already
                // downloaded, so another network round-trip would buy nothing.
                await databaseInitializer.ReseedAsync();
                await notificationWriter.DismissByTriggerAsync(
                    NotificationDismissTrigger.Reseed, NotificationResolution.Reseeded);
                break;
            }
            // #303: the coarse, whole-batch form of the two options the review page offers per action:
            // keep everything as stored, or take everything the file brought. Interim by design: the
            // notification will eventually point at an item-by-item resolution UX (#66) rather than
            // deciding here, and these exist so the common case (fix the file, reseed) is not the only
            // route out of a conflict.
            case NotificationDismissTrigger.ImportReviewResolved:
            {
                if (metadata is not ImportReviewPendingMetadataDto review)
                    throw new InvalidOperationException("An import-review action needs the alert's own payload to know which batch it resolves.");

                // No default side. Choosing one here would silently overwrite the operator's data with
                // whichever way the code happened to lean.
                if (choice is not FieldResolutionChoice resolution)
                    throw new InvalidOperationException("An import-review action needs an explicit choice; keeping and replacing are not interchangeable.");

                await importActions.DecideBatchAsync(review.BatchId, resolution);

                // Deciding stages the choice; it does not write it. Applying is the completion of the
                // decision the operator just confirmed (the dialog says the action cannot be undone,
                // which is only true once it has landed), and it is what dismisses this alert, since
                // dismissal is wired to ApplyBatchAsync rather than to deciding.
                // #308: the resolution rides the apply, because applying is what dismisses the alert.
                // Recording it beforehand would mark a notification resolved even when the apply failed;
                // recording it afterwards would need a second write against a row already dismissed.
                // Only this caller knows which side was chosen; the REST apply path decides per field
                // and passes none.
                await importActions.ApplyBatchAsync(
                    review.BatchId,
                    resolution: resolution is FieldResolutionChoice.Keep
                        ? NotificationResolution.KeptExisting
                        : NotificationResolution.TookIncoming);
                break;
            }
            default:
                throw new NotSupportedException($"No executable action is wired up for trigger '{trigger}'.");
        }

        return NotificationActionResult.Success();
    }

    private const string ReseedTag = "[Notifications - Reseed]";

    // #348: what has to happen before a reseed, per the option the user chose. Stops with the obstacle,
    // before anything is reseeded, whenever the backup that option relies on cannot be taken.
    private async Task<NotificationActionResult> PrepareReseedAsync(NotificationActionOption option)
    {
        if (option is NotificationActionOption.ReseedWithoutBackup)
        {
            // The option is the user's permission, given after being shown why no backup can be taken.
            // What they agreed to go without is recorded where a later "where is the backup" question
            // finds it: the log, and the audit trail that outlives it.
            logger.LogReseedProceedingWithoutBackup(databaseInitializer.CheckBackupReadiness().ToString());
            await auditWriter.WriteAsync(new AuditEntryEntity
            {
                TableName   = "Database",
                Operation   = AuditOperation.BackupSkipped,
                Agent       = callerContext.Agent,
                PerformedAt = DateTime.UtcNow,
            });
            return NotificationActionResult.Success();
        }

        if (option is NotificationActionOption.RemoveOldestBackupThenReseed)
        {
            // Newest first, so the oldest is last: the one the option offered to remove, and no other.
            IReadOnlyList<BackupFileInfo> backups = backupReader.List();
            if (backups.Count > 0 && await backupOperations.RemoveAsync(backups[^1].Name, ReseedTag) is not BackupDeleteOutcome.Deleted)
                return NotificationActionResult.RefusedForBackup(databaseInitializer.CheckBackupReadiness());
        }

        DatabaseBackupResult backup = await backupOperations.CreateAsync(ReseedTag);
        return backup.Succeeded
            ? NotificationActionResult.Success()
            : NotificationActionResult.RefusedForBackup(backup.Outcome);
    }
}

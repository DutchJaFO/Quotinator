using Microsoft.Extensions.Logging;
using Quotinator.Api.Startup;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Enums;
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
internal sealed class NotificationActionExecutor(
    IDatabaseInitializer databaseInitializer, DatabaseHealthState databaseHealth, INotificationWriter notificationWriter,
    IAppVersionTracker appVersionTracker, IVersionService versionService, ILogger<NotificationActionExecutor> logger,
    IImportActionService importActions) : INotificationActionExecutor
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
    public async Task ExecuteAsync(NotificationDismissTrigger trigger, NotificationMetadataDto? metadata = null, FieldResolutionChoice? choice = null)
    {
        switch (trigger)
        {
            // DatabaseReset takes no parameters from the notification: a schema-version overshoot is
            // resolved by truing up the whole database's version bookkeeping, so there is nothing for
            // the payload to narrow. metadata is accepted and ignored here rather than absent from the
            // contract, so #304's Reseed — which genuinely needs "reseed *this* file" — is a new case
            // in this switch instead of an interface change rippling through every caller.
            case NotificationDismissTrigger.DatabaseReset:
                await databaseInitializer.ResetAsync();
                databaseHealth.MarkHealthy();
                await notificationWriter.DismissByTriggerAsync(NotificationDismissTrigger.DatabaseReset);
                // #81: matches AdminEndpoints.cs's own POST /admin/database/reset wiring — Reset
                // rebuilds System_AppVersion empty like every other table, so re-populate it
                // immediately rather than leaving it empty until the next full app restart.
                // Non-fatal, same reasoning as AdminEndpoints.cs's own try/catch around this call.
                try
                {
                    await appVersionTracker.RecordCurrentAsync(versionService.Application, versionService.Version);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[Server] Failed to record the current app version after Reset — non-fatal, the reset itself still succeeded.");
                }
                break;
            // #304. Deliberately not a copy of the case above: a reseed replaces content within an
            // intact schema, so it neither degrades health nor empties System_AppVersion, and calling
            // MarkHealthy or RecordCurrentAsync here would assert a recovery that never happened.
            //
            // metadata carries which files changed (ReseedRecommendedMetadataDto), and is ignored for
            // now — IDatabaseInitializer.ReseedAsync has no per-file overload, so there is nothing to
            // narrow to. The payload reaching this far is what makes adding one later a change to this
            // case rather than to the contract.
            case NotificationDismissTrigger.Reseed:
                // Default forceSourceRefresh: the content that prompted the recommendation is already
                // downloaded, so another network round-trip would buy nothing.
                await databaseInitializer.ReseedAsync();
                await notificationWriter.DismissByTriggerAsync(NotificationDismissTrigger.Reseed);
                break;
            // #303: the coarse, whole-batch form of the two options the review page offers per action —
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
                    throw new InvalidOperationException("An import-review action needs an explicit choice — keeping and replacing are not interchangeable.");

                await importActions.DecideBatchAsync(review.BatchId, resolution);

                // Deciding stages the choice; it does not write it. Applying is the completion of the
                // decision the operator just confirmed — the dialog says the action cannot be undone,
                // which is only true once it has landed — and it is what dismisses this alert, since
                // dismissal is wired to ApplyBatchAsync rather than to deciding.
                await importActions.ApplyBatchAsync(review.BatchId);
                break;
            }
            default:
                throw new NotSupportedException($"No executable action is wired up for trigger '{trigger}'.");
        }
    }
}

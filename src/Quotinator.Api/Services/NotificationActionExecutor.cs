using Microsoft.Extensions.Logging;
using Quotinator.Api.Startup;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Enums;
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
internal sealed class NotificationActionExecutor(
    IDatabaseInitializer databaseInitializer, DatabaseHealthState databaseHealth, INotificationWriter notificationWriter,
    IAppVersionTracker appVersionTracker, IVersionService versionService, ILogger<NotificationActionExecutor> logger) : INotificationActionExecutor
{
    /// <inheritdoc/>
    public bool CanExecute(NotificationDismissTrigger trigger) => trigger switch
    {
        NotificationDismissTrigger.DatabaseReset => true,
        _                                         => false,
    };

    /// <inheritdoc/>
    public async Task ExecuteAsync(NotificationDismissTrigger trigger)
    {
        switch (trigger)
        {
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
                    await appVersionTracker.RecordCurrentVersionAsync(versionService.Version);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[Server] Failed to record the current app version after Reset — non-fatal, the reset itself still succeeded.");
                }
                break;
            default:
                throw new NotSupportedException($"No executable action is wired up for trigger '{trigger}'.");
        }
    }
}

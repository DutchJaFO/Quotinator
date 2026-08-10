using Quotinator.Api.Startup;
using Quotinator.Data.Database;
using Quotinator.Data.Enums;
using Quotinator.Data.Repositories;

namespace Quotinator.Api.Services;

/// <inheritdoc cref="INotificationActionExecutor"/>
/// <remarks>Initialises the executor with every dependency an executable trigger might need.</remarks>
/// <param name="databaseInitializer">Runs the Reset action for <see cref="NotificationDismissTrigger.DatabaseReset"/>.</param>
/// <param name="databaseHealth">Marked healthy after a successful Reset, matching <c>AdminEndpoints.cs</c>'s own <c>POST /admin/database/reset</c> handler.</param>
/// <param name="notificationWriter">Dismisses any notification carrying the trigger just executed, matching <c>AdminEndpoints.cs</c>'s own reset-success wiring (#278 Step 6).</param>
internal sealed class NotificationActionExecutor(
    IDatabaseInitializer databaseInitializer, DatabaseHealthState databaseHealth, INotificationWriter notificationWriter) : INotificationActionExecutor
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
                break;
            default:
                throw new NotSupportedException($"No executable action is wired up for trigger '{trigger}'.");
        }
    }
}

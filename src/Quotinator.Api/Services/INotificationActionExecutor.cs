using Quotinator.Data.Enums;

namespace Quotinator.Api.Services;

/// <summary>
/// Executes the concrete server-side action a notification's <see cref="NotificationDismissTrigger"/>
/// references — invoked from the Blazor Notifications page's Action column (#278), never from the
/// read-only startup-modal summary. One case per trigger; extend by adding a case (and whatever
/// dependency that action needs) to <see cref="NotificationActionExecutor"/> when a second trigger
/// type is introduced — that switch is the single place mapping a trigger to real work.
/// </summary>
internal interface INotificationActionExecutor
{
    /// <summary>
    /// Whether <paramref name="trigger"/> has an executable action wired up, as opposed to a
    /// purely informational/dismiss-only notification with no corresponding server action.
    /// </summary>
    bool CanExecute(NotificationDismissTrigger trigger);

    /// <summary>Executes the action associated with <paramref name="trigger"/>.</summary>
    Task ExecuteAsync(NotificationDismissTrigger trigger);
}

using Quotinator.Data.Enums;
using Quotinator.Data.Notifications;

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

    /// <summary>
    /// Executes the action associated with <paramref name="trigger"/>, given the originating
    /// notification's own payload.
    /// </summary>
    /// <param name="trigger">Which action to run.</param>
    /// <param name="metadata">
    /// The originating notification's metadata, or <see langword="null"/> when it has none (every row
    /// written before #312, and any notification whose action needs no parameters). This is what lets
    /// an action operate on something specific rather than only ever on everything — #304's
    /// <c>Reseed</c> needs to mean "reseed *this* file", which a bare trigger cannot express.
    /// <para>
    /// Deliberately the payload rather than the <c>NotificationEntity</c>: a later milestone wants
    /// transient, non-persisted notifications, and this contract must not assume every notification is
    /// a database row.
    /// </para>
    /// </param>
    /// <param name="choice">
    /// Which side wins, for a trigger whose action offers more than one outcome (#303's
    /// <see cref="NotificationDismissTrigger.ImportReviewResolved"/> — keep what is stored, or take what
    /// the file brought). <see langword="null"/> for every trigger with a single outcome; a trigger that
    /// needs one and does not get it throws rather than picking a side on the operator's behalf.
    /// </param>
    Task ExecuteAsync(NotificationDismissTrigger trigger, NotificationMetadataDto? metadata = null, FieldResolutionChoice? choice = null);
}

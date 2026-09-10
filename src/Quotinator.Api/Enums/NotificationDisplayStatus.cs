namespace Quotinator.Api.Enums;

/// <summary>
/// The mutually-exclusive display states a notification's Status column shows, and the Notifications
/// page's filter selects on.
/// </summary>
/// <remarks>
/// Not a persisted column: computed at render time by
/// <see cref="Components.Controls.NotificationTable.GetDisplayStatus"/> from the row's own dismissal and
/// expiry, plus two facts from outside the row — whether its action is running (#367), and whether its
/// action can still be carried out (#369).
/// </remarks>
internal enum NotificationDisplayStatus
{
    /// <summary>Undismissed and unexpired, and nothing stops its action from running.</summary>
    Active,

    /// <summary>Past its expiry without having been dismissed.</summary>
    Expired,

    /// <summary>Dismissed by the user, or dismissed before a reason was recorded.</summary>
    Dismissed,

    /// <summary>Dismissed because its action was carried out (#304).</summary>
    Resolved,

    /// <summary>Dismissed because its subject no longer exists (#303).</summary>
    Obsolete,

    /// <summary>Its action is running right now (#367).</summary>
    Executing,

    /// <summary>Still active, but what its action acts on is gone, so the action can no longer run (#369).</summary>
    ActionUnavailable,
}

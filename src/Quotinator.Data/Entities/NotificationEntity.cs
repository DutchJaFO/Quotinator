using Dapper.Contrib.Extensions;
using Quotinator.Data.Enums;
using Quotinator.Data.Models;

namespace Quotinator.Data.Entities;

/// <summary>
/// A persisted, non-fatal/informational/action-needed message surfaced at startup and reviewable via
/// its own history page and REST endpoints (#278). <see cref="RecordBase.DateCreated"/> is the
/// notification's own created-at timestamp — no separate duplicate column, unlike
/// <see cref="AuditEntryEntity"/>'s own documented <c>PerformedAt</c>/<c>DateCreated</c> redundancy,
/// since nothing here needs a timestamp distinct from creation.
/// </summary>
[Table("System_Notification")]
public sealed class NotificationEntity : RecordBase
{
    /// <summary>Severity/kind of this notification.</summary>
    public SafeValue<NotificationType?> Type { get; init; } = SafeValue<NotificationType?>.Empty;

    /// <summary>The specific message text — carries the concrete reason/recommendation, not the <see cref="Type"/>.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// When this notification stops being considered active. Always populated at write time — either
    /// an explicit value, or the configured default (<c>Quotinator:NotificationDefaultExpiryHours</c>)
    /// applied by <see cref="Repositories.INotificationWriter"/> when none is supplied.
    /// </summary>
    public SafeValue<DateTime?> ExpiresAt { get; init; } = SafeValue<DateTime?>.Empty;

    /// <summary>Whether this notification has been dismissed. Mirrors <see cref="RecordBase.IsDeleted"/>/<see cref="RecordBase.DateDeleted"/>'s own flag-plus-timestamp pairing style.</summary>
    public bool IsDismissed { get; set; }

    /// <summary>When this notification was dismissed, or <see langword="null"/> if it hasn't been.</summary>
    public SafeValue<DateTime?> DismissedAt { get; set; } = SafeValue<DateTime?>.Empty;

    /// <summary>
    /// Which action, if performed, supersedes this notification — see
    /// <see cref="Enums.NotificationDismissTrigger"/>. <see langword="null"/> when nothing dismisses
    /// this notification automatically.
    /// </summary>
    public SafeValue<NotificationDismissTrigger?> DismissTriggerKey { get; init; } = SafeValue<NotificationDismissTrigger?>.Empty;
}

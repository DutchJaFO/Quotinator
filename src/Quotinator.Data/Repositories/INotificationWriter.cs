using Quotinator.Data.Entities;
using Quotinator.Data.Enums;

namespace Quotinator.Data.Repositories;

/// <summary>Write-side operations for <see cref="NotificationEntity"/> (#278).</summary>
public interface INotificationWriter
{
    /// <summary>
    /// Creates and persists a new notification. When <paramref name="expiresAt"/> is omitted, the
    /// configured default expiry duration (<c>Quotinator:NotificationDefaultExpiryHours</c>) applies.
    /// </summary>
    Task<NotificationEntity> WriteAsync(
        NotificationType type, string message, DateTime? expiresAt = null, NotificationDismissTrigger? dismissTrigger = null);

    /// <summary>
    /// Marks a single notification dismissed by Id. Returns the updated entity, or <see langword="null"/>
    /// when no notification with that Id exists.
    /// </summary>
    Task<NotificationEntity?> DismissAsync(Guid id);

    /// <summary>
    /// Marks every active (undismissed, non-deleted) notification carrying <paramref name="trigger"/>
    /// dismissed — #278's "dismiss on related action" mechanism, e.g. called by
    /// <c>POST /admin/database/reset</c>'s own success path with
    /// <see cref="NotificationDismissTrigger.DatabaseReset"/>. Returns the number of rows dismissed;
    /// <c>0</c> when nothing matched (a deliberate no-op, not an error).
    /// </summary>
    Task<int> DismissByTriggerAsync(NotificationDismissTrigger trigger);
}

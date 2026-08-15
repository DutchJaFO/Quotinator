using Quotinator.Data.Entities;
using Quotinator.Data.Enums;

namespace Quotinator.Data.Repositories;

/// <summary>Write-side operations for <see cref="NotificationEntity"/> (#278).</summary>
public interface INotificationWriter
{
    /// <summary>
    /// Creates and persists a new notification.
    /// <para>
    /// <paramref name="expiresAt"/> is genuinely optional as of #312: omitting it means the
    /// notification does not expire. It previously meant "apply the configured default"
    /// (<c>Quotinator:NotificationDefaultExpiryHours</c>), which silently aged out notifications about
    /// real, still-unresolved conditions. A producer that wants time-limited behaviour now asks for it
    /// explicitly.
    /// </para>
    /// </summary>
    /// <param name="type">Severity/kind.</param>
    /// <param name="body">The message text.</param>
    /// <param name="title">Optional short headline shown above <paramref name="body"/>.</param>
    /// <param name="expiresAt">When this notification stops being active. <see langword="null"/> means it never expires.</param>
    /// <param name="dismissTrigger">Which action, if performed, supersedes this notification.</param>
    /// <param name="metadata">Free-form producer-owned JSON payload. Requires <paramref name="metadataKind"/> when supplied.</param>
    /// <param name="metadataKind">Names the shape of <paramref name="metadata"/>.</param>
    /// <param name="appVersionId">The <c>System_AppVersion</c> row for the app version adding this notification.</param>
    Task<NotificationEntity> WriteAsync(
        NotificationType type,
        string body,
        string? title = null,
        DateTime? expiresAt = null,
        NotificationDismissTrigger? dismissTrigger = null,
        string? metadata = null,
        NotificationMetadataKind? metadataKind = null,
        Guid? appVersionId = null);

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

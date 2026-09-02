using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Notifications;

namespace Quotinator.Data.Repositories;

/// <summary>Write-side operations for <see cref="NotificationEntity"/> (#278).</summary>
public interface INotificationWriter
{
    /// <summary>
    /// Creates and persists a new notification.
    /// <para>
    /// <paramref name="expiresAt"/> is always optional as of #312: omitting it means the notification
    /// does not expire. It previously meant "apply the configured default", which silently aged out
    /// notifications about real, still-unresolved conditions. A producer that wants time-limited
    /// behaviour now asks for it explicitly, and no configured default exists to fall back on.
    /// </para>
    /// <para>
    /// <paramref name="appVersionId"/> has no default, deliberately. It is nullable — provenance is
    /// genuinely unknown when recording the current version failed — but a caller has to *say* so.
    /// A payload cannot exist without an identity, because <c>IdentityComponents</c> is abstract;
    /// provenance had no equivalent guarantee while it could be omitted by saying nothing, and was duly
    /// omitted, leaving v1.8.3's shipped notification unattributed until a later migration repaired it.
    /// </para>
    /// </summary>
    /// <param name="type">Severity/kind.</param>
    /// <param name="body">The message text.</param>
    /// <param name="appVersionId">The <c>System_AppVersion</c> row for the app version adding this notification, or <see langword="null"/> when it could not be determined.</param>
    /// <param name="title">Optional short headline shown above <paramref name="body"/>.</param>
    /// <param name="expiresAt">When this notification stops being active. <see langword="null"/> means it never expires.</param>
    /// <param name="dismissTrigger">Which action, if performed, supersedes this notification.</param>
    /// <param name="metadata">Free-form producer-owned JSON payload. Requires <paramref name="metadataKind"/> when supplied.</param>
    /// <param name="metadataKind">Names the shape of <paramref name="metadata"/>.</param>
    /// <param name="translations">Every non-original language's title and body, written as sibling rows in the same transaction (#319). The original language is never supplied here.</param>
    Task<NotificationEntity> WriteAsync(
        NotificationType type,
        string body,
        Guid? appVersionId,
        string? title = null,
        DateTime? expiresAt = null,
        NotificationDismissTrigger? dismissTrigger = null,
        string? metadata = null,
        NotificationMetadataKind? metadataKind = null,
        IReadOnlyList<NotificationTranslation>? translations = null);

    /// <summary>
    /// Marks a single notification dismissed by Id. Returns the updated entity, or <see langword="null"/>
    /// when no notification with that Id exists.
    /// </summary>
    Task<NotificationEntity?> DismissAsync(Guid id, string? language = null);

    /// <summary>
    /// Marks every active (undismissed, non-deleted) notification carrying <paramref name="trigger"/>
    /// dismissed — #278's "dismiss on related action" mechanism, e.g. called by
    /// <c>POST /admin/database/reset</c>'s own success path with
    /// <see cref="NotificationDismissTrigger.DatabaseReset"/>. Returns the number of rows dismissed;
    /// <c>0</c> when nothing matched (a deliberate no-op, not an error).
    /// </summary>
    Task<int> DismissByTriggerAsync(NotificationDismissTrigger trigger);

    /// <summary>
    /// Marks dismissed every active notification carrying <paramref name="trigger"/> whose metadata
    /// payload names <paramref name="batchId"/> (#303). Returns the number of rows dismissed; <c>0</c>
    /// when nothing matched, which is a deliberate no-op rather than an error.
    /// </summary>
    /// <remarks>
    /// The narrow sibling of <see cref="DismissByTriggerAsync"/>, and not a convenience: several files
    /// can each leave a batch awaiting review at once, so the trigger alone selects too much —
    /// resolving one batch would dismiss every other file's alert as well. The trigger says *what kind*
    /// of resolution applies; the batch id says *which* notification it resolved.
    /// </remarks>
    /// <param name="trigger">The trigger the notification must carry.</param>
    /// <param name="batchId">The import batch named in the notification's own payload.</param>
    /// <param name="reason">Why it stopped being active — resolved by the review, or obsolete because the batch is gone.</param>
    /// <param name="resolution">#308: how the action settled it, where <paramref name="reason"/> says only that it settled. <see langword="null"/> whenever no action ran — a superseded batch records a reason and no resolution.</param>
    Task<int> DismissByTriggerAndBatchAsync(NotificationDismissTrigger trigger, string batchId, NotificationDismissReason reason, NotificationResolution? resolution = null);
}

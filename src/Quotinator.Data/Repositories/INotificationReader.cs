using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Models;

namespace Quotinator.Data.Repositories;

/// <summary>Read-side operations for <see cref="NotificationEntity"/> (#278).</summary>
public interface INotificationReader
{
    /// <summary>
    /// Returns every undismissed, unexpired, non-deleted notification, newest first — the set
    /// surfaced in <c>StartupSuccessModal</c>/<c>StartupErrorModal</c>.
    /// </summary>
    Task<IReadOnlyList<NotificationEntity>> GetActiveNotificationsAsync(string? language = null);

    /// <summary>
    /// Returns a paginated page of the full notification history (including dismissed/expired,
    /// excluding only soft-deleted rows), newest first — backs <c>GET /api/v1/notifications</c> and
    /// the Blazor Notifications page.
    /// </summary>
    Task<PagedItems<NotificationEntity>> GetPagedAsync(int page, int pageSize, string? language = null);

    /// <summary>
    /// Every non-deleted notification of <paramref name="kind"/>, dismissed and expired included, newest
    /// first (#369).
    /// </summary>
    /// <remarks>
    /// A notification's payload is written to outlive the records it names, so this is how a caller
    /// recovers what a notification recorded about something that is now gone — which is why a dismissed
    /// row is not filtered out.
    /// </remarks>
    /// <param name="kind">The metadata kind to return.</param>
    /// <param name="language">The language to resolve title and body to, falling back to each row's original.</param>
    Task<IReadOnlyList<NotificationEntity>> GetByMetadataKindAsync(NotificationMetadataKind kind, string? language = null);
}

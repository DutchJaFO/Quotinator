using Quotinator.Data.Entities;
using Quotinator.Data.Models;

namespace Quotinator.Data.Repositories;

/// <summary>Read-side operations for <see cref="NotificationEntity"/> (#278).</summary>
public interface INotificationReader
{
    /// <summary>
    /// Returns every undismissed, unexpired, non-deleted notification, newest first — the set
    /// surfaced in <c>StartupSuccessModal</c>/<c>StartupErrorModal</c>.
    /// </summary>
    Task<IReadOnlyList<NotificationEntity>> GetActiveNotificationsAsync();

    /// <summary>
    /// Returns a paginated page of the full notification history (including dismissed/expired,
    /// excluding only soft-deleted rows), newest first — backs <c>GET /api/v1/notifications</c> and
    /// the Blazor Notifications page.
    /// </summary>
    Task<PagedItems<NotificationEntity>> GetPagedAsync(int page, int pageSize);
}

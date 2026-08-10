using Quotinator.Data.Entities;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>In-memory <see cref="INotificationReader"/> for endpoint tests (#278) — mirrors <c>FakeFileResourceRepository</c>'s own shape.</summary>
internal sealed class FakeNotificationReader : INotificationReader
{
    private readonly List<NotificationEntity> _notifications = [];

    /// <summary>Registers a fixed notification for a test to look up.</summary>
    public void Seed(NotificationEntity notification) => _notifications.Add(notification);

    public Task<IReadOnlyList<NotificationEntity>> GetActiveNotificationsAsync()
        => Task.FromResult<IReadOnlyList<NotificationEntity>>([.. _notifications.Where(n => !n.IsDismissed)]);

    public Task<PagedItems<NotificationEntity>> GetPagedAsync(int page, int pageSize)
    {
        var ordered = _notifications.OrderByDescending(n => n.DateCreated.Parsed).ToList();
        var total   = ordered.Count;

        List<NotificationEntity> items = pageSize == 0
            ? ordered
            : [.. ordered.Skip((page - 1) * pageSize).Take(pageSize)];

        var effectivePageSize = pageSize == 0 ? items.Count : pageSize;
        return Task.FromResult(new PagedItems<NotificationEntity>(items, page, effectivePageSize, total));
    }
}

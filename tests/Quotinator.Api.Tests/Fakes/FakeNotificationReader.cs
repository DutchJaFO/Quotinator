using Quotinator.Data.Entities;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>In-memory <see cref="INotificationReader"/> for endpoint tests (#278) — mirrors <c>FakeFileResourceRepository</c>'s own shape.</summary>
internal sealed class FakeNotificationReader : INotificationReader
{
    private readonly List<NotificationEntity> _notifications = [];

    /// <summary>
    /// The language the last read asked for (#319). Recorded rather than ignored so an endpoint test
    /// can assert which language the endpoint actually resolved — `?lang=` winning over
    /// <c>Accept-Language</c> is a claim about what reaches the reader, and a fake that swallowed the
    /// argument could not tell a correct endpoint from one that never passed it on.
    /// </summary>
    public string? LastRequestedLanguage { get; private set; }

    /// <summary>Registers a fixed notification for a test to look up.</summary>
    public void Seed(NotificationEntity notification) => _notifications.Add(notification);

    public Task<IReadOnlyList<NotificationEntity>> GetActiveNotificationsAsync(string? language = null)
    {
        LastRequestedLanguage = language;
        return Task.FromResult<IReadOnlyList<NotificationEntity>>([.. _notifications.Where(n => !n.IsDismissed)]);
    }

    public Task<PagedItems<NotificationEntity>> GetPagedAsync(int page, int pageSize, string? language = null)
    {
        LastRequestedLanguage = language;

        List<NotificationEntity> ordered = [.. _notifications.OrderByDescending(n => n.DateCreated.Parsed)];
        int total = ordered.Count;

        List<NotificationEntity> items = pageSize == 0
            ? ordered
            : [.. ordered.Skip((page - 1) * pageSize).Take(pageSize)];

        int effectivePageSize = pageSize == 0 ? items.Count : pageSize;
        return Task.FromResult(new PagedItems<NotificationEntity>(items, page, effectivePageSize, total));
    }
}

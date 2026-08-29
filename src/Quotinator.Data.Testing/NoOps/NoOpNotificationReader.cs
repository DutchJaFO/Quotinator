using Quotinator.Data.Entities;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Testing.NoOps;

/// <summary>No-op <see cref="INotificationReader"/> for use in unit tests that do not exercise notification read behaviour — always returns an empty result.</summary>
public sealed class NoOpNotificationReader : INotificationReader
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly NoOpNotificationReader Instance = new();

    /// <inheritdoc/>
    public Task<IReadOnlyList<NotificationEntity>> GetActiveNotificationsAsync(string? language = null)
        => Task.FromResult<IReadOnlyList<NotificationEntity>>([]);

    /// <inheritdoc/>
    public Task<PagedItems<NotificationEntity>> GetPagedAsync(int page, int pageSize, string? language = null)
        => Task.FromResult(new PagedItems<NotificationEntity>([], page, pageSize, 0));
}

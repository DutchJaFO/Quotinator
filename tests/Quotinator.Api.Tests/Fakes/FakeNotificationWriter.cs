using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>In-memory <see cref="INotificationWriter"/> for endpoint tests (#278).</summary>
internal sealed class FakeNotificationWriter : INotificationWriter
{
    private readonly Dictionary<Guid, NotificationEntity> _notifications = [];

    /// <summary>Records the trigger every <see cref="DismissByTriggerAsync"/> call was made with — lets a wiring test confirm the caller passed the right value without depending on database side effects.</summary>
    public List<NotificationDismissTrigger> DismissByTriggerCalls { get; } = [];

    /// <summary>Registers a fixed notification for a test to look up by id.</summary>
    public void Seed(NotificationEntity notification) => _notifications[notification.Id] = notification;

    public Task<NotificationEntity> WriteAsync(
        NotificationType type, string message, DateTime? expiresAt = null, NotificationDismissTrigger? dismissTrigger = null)
    {
        var entity = new NotificationEntity
        {
            Type    = new SafeValue<NotificationType?>(type.ToString(), type),
            Message = message,
        };
        _notifications[entity.Id] = entity;
        return Task.FromResult(entity);
    }

    public Task<NotificationEntity?> DismissAsync(Guid id)
    {
        if (!_notifications.TryGetValue(id, out var entity))
            return Task.FromResult<NotificationEntity?>(null);

        entity.IsDismissed = true;
        entity.DismissedAt = SafeDateValue.Now;
        return Task.FromResult<NotificationEntity?>(entity);
    }

    public Task<int> DismissByTriggerAsync(NotificationDismissTrigger trigger)
    {
        DismissByTriggerCalls.Add(trigger);
        return Task.FromResult(0);
    }
}

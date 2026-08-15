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

    /// <summary>Records every message passed to <see cref="WriteAsync"/> — lets a test confirm whether a write actually happened without depending on database side effects.</summary>
    public List<string> WrittenMessages { get; } = [];

    /// <summary>Registers a fixed notification for a test to look up by id.</summary>
    public void Seed(NotificationEntity notification) => _notifications[notification.Id] = notification;

    /// <summary>Records the metadata passed to each <see cref="WriteAsync"/> call, so a test can assert on structured payloads rather than message text.</summary>
    public List<(string? Metadata, NotificationMetadataKind? Kind)> WrittenMetadata { get; } = [];

    public Task<NotificationEntity> WriteAsync(
        NotificationType type,
        string body,
        string? title = null,
        DateTime? expiresAt = null,
        NotificationDismissTrigger? dismissTrigger = null,
        string? metadata = null,
        NotificationMetadataKind? metadataKind = null,
        Guid? appVersionId = null)
    {
        WrittenMessages.Add(body);
        WrittenMetadata.Add((metadata, metadataKind));
        var entity = new NotificationEntity
        {
            Type     = new SafeValue<NotificationType?>(type.ToString(), type),
            Title    = title,
            Body     = body,
            Metadata = metadata,
            MetadataKind = metadataKind is null
                ? SafeValue<NotificationMetadataKind?>.Empty
                : new SafeValue<NotificationMetadataKind?>(metadataKind.Value.ToString(), metadataKind),
            AppVersionId = appVersionId,
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

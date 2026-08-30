using Quotinator.Data.Notifications;
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

    /// <summary>
    /// Records the translation set passed to each <see cref="WriteAsync"/> call (#319), so a test can
    /// assert that a producer's translations reached the writer rather than only that it built them.
    /// </summary>
    public List<IReadOnlyList<NotificationTranslation>> WrittenTranslations { get; } = [];
    /// <summary>Records the metadata passed to each <see cref="WriteAsync"/> call, so a test can assert on structured payloads rather than message text.</summary>
    public List<(string? Metadata, NotificationMetadataKind? Kind)> WrittenMetadata { get; } = [];

    public Task<NotificationEntity> WriteAsync(
        NotificationType type,
        string body,
        Guid? appVersionId,
        string? title = null,
        DateTime? expiresAt = null,
        NotificationDismissTrigger? dismissTrigger = null,
        string? metadata = null,
        NotificationMetadataKind? metadataKind = null,
        IReadOnlyList<NotificationTranslation>? translations = null)
    {
        WrittenMessages.Add(body);
        WrittenMetadata.Add((metadata, metadataKind));
        NotificationEntity entity = new NotificationEntity
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
        WrittenTranslations.Add(translations ?? []);
        _notifications[entity.Id] = entity;
        return Task.FromResult(entity);
    }

    /// <summary>The language the last dismiss asked for (#319) — recorded so an endpoint test can assert
    /// the endpoint actually passed it on rather than swallowing it.</summary>
    public string? LastRequestedLanguage { get; private set; }

    public Task<NotificationEntity?> DismissAsync(Guid id, string? language = null)
    {
        LastRequestedLanguage = language;

        if (!_notifications.TryGetValue(id, out NotificationEntity? entity))
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

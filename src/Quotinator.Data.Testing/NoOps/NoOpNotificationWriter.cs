using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Testing.NoOps;

/// <summary>No-op <see cref="INotificationWriter"/> for use in unit tests that do not exercise notification write behaviour.</summary>
public sealed class NoOpNotificationWriter : INotificationWriter
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly NoOpNotificationWriter Instance = new();

    /// <inheritdoc/>
    public Task<NotificationEntity> WriteAsync(
        NotificationType type,
        string body,
        Guid? appVersionId,
        string? title = null,
        DateTime? expiresAt = null,
        NotificationDismissTrigger? dismissTrigger = null,
        string? metadata = null,
        NotificationMetadataKind? metadataKind = null)
        => Task.FromResult(new NotificationEntity
        {
            Type  = new SafeValue<NotificationType?>(type.ToString(), type),
            Title = title,
            Body  = body,
        });

    /// <inheritdoc/>
    public Task<NotificationEntity?> DismissAsync(Guid id)
        => Task.FromResult<NotificationEntity?>(null);

    /// <inheritdoc/>
    public Task<int> DismissByTriggerAsync(NotificationDismissTrigger trigger)
        => Task.FromResult(0);
}

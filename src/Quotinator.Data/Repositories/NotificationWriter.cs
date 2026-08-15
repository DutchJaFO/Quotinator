using Dapper;
using Dapper.Contrib.Extensions;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Models;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Repositories;

/// <summary>
/// SQLite implementation of <see cref="INotificationWriter"/>. Extends
/// <see cref="SqliteRepositoryBase{T}"/> directly — NOT <see cref="SqliteRepository{T}"/> — a
/// notification write is not itself an auditable domain change, matching
/// <see cref="AuditEntryWriter"/>'s own precedent. Dapper.Contrib generates the INSERT statement from
/// the <c>[Table]</c> attribute on <see cref="NotificationEntity"/> and the <c>[ExplicitKey]</c> it
/// inherits from <see cref="Models.RecordBase"/>; no SQL string is required for writes.
/// </summary>
/// <remarks>Initialises the writer with the connection factory and the configured default expiry.</remarks>
/// <param name="factory">Factory used to open SQLite connections.</param>
/// <param name="defaultExpiryHours">
/// Applied when <see cref="WriteAsync"/> is called with no explicit <c>expiresAt</c> — sourced from
/// <c>Quotinator:NotificationDefaultExpiryHours</c> (falling back to
/// <c>QueryParamDefaults.NotificationDefaultExpiryHours</c>) at DI registration time.
/// </param>
public sealed class NotificationWriter(IDbConnectionFactory factory, int defaultExpiryHours)
    : SqliteRepositoryBase<NotificationEntity>(factory), INotificationWriter
{
    private readonly int _defaultExpiryHours = defaultExpiryHours;

    /// <inheritdoc/>
    public async Task<NotificationEntity> WriteAsync(
        NotificationType type,
        string body,
        string? title = null,
        DateTime? expiresAt = null,
        NotificationDismissTrigger? dismissTrigger = null,
        string? metadata = null,
        NotificationMetadataKind? metadataKind = null,
        Guid? appVersionId = null)
    {
        var entity = new NotificationEntity
        {
            Type  = new SafeValue<NotificationType?>(type.ToString(), type),
            Title = title,
            Body  = body,
            // #312: no expiry unless the caller asks for one. Previously this applied
            // _defaultExpiryHours whenever expiresAt was null, so every notification silently aged out
            // — including ones describing conditions that were still unresolved.
            ExpiresAt         = expiresAt is null ? SafeDateValue.Empty : SafeDateValue.From(expiresAt.Value),
            DismissTriggerKey = dismissTrigger is null
                ? SafeValue<NotificationDismissTrigger?>.Empty
                : new SafeValue<NotificationDismissTrigger?>(dismissTrigger.Value.ToString(), dismissTrigger),
            Metadata     = metadata,
            MetadataKind = metadataKind is null
                ? SafeValue<NotificationMetadataKind?>.Empty
                : new SafeValue<NotificationMetadataKind?>(metadataKind.Value.ToString(), metadataKind),
            AppVersionId = appVersionId,
        };

        using var conn = Factory.CreateConnection();
        conn.Open();
        await conn.InsertAsync(entity);
        return entity;
    }

    /// <inheritdoc/>
    public async Task<NotificationEntity?> DismissAsync(Guid id)
    {
        using var conn = Factory.CreateConnection();
        conn.Open();

        var entity = await conn.QuerySingleOrDefaultAsync<NotificationEntity>(Sql.Notifications.SelectById, new { id });
        if (entity is null)
            return null;

        var now = SafeDateValue.Now;
        await conn.ExecuteAsync(Sql.Notifications.UpdateDismissById,
            new { id, dismissedAt = now.Raw, dateModified = now.Raw });

        entity.IsDismissed = true;
        entity.DismissedAt = now;
        return entity;
    }

    /// <inheritdoc/>
    public async Task<int> DismissByTriggerAsync(NotificationDismissTrigger trigger)
    {
        using var conn = Factory.CreateConnection();
        conn.Open();

        var now = SafeDateValue.Now;
        return await conn.ExecuteAsync(Sql.Notifications.UpdateDismissByTrigger,
            new { trigger = trigger.ToString(), dismissedAt = now.Raw, dateModified = now.Raw });
    }
}

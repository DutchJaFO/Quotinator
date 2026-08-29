using System.Data;
using Dapper;
using Dapper.Contrib.Extensions;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Models;
using Quotinator.Data.Notifications;
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
/// <remarks>Initialises the writer with the connection factory.</remarks>
/// <param name="factory">Factory used to open SQLite connections.</param>
public sealed class NotificationWriter(IDbConnectionFactory factory)
    : SqliteRepositoryBase<NotificationEntity>(factory), INotificationWriter
{
    /// <inheritdoc/>
    public async Task<NotificationEntity> WriteAsync(
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
        NotificationEntity entity = new NotificationEntity
        {
            Type  = new SafeValue<NotificationType?>(type.ToString(), type),
            Title = title,
            Body  = body,
            // #312: expiry is always optional. This previously applied a configured default whenever
            // expiresAt was null, so every notification silently aged out — including ones describing
            // conditions that were still unresolved.
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

        using IDbConnection conn = Factory.CreateConnection();
        conn.Open();

        // One transaction: a notification whose translations failed to land would read as English to
        // every non-English user with nothing to indicate the write was incomplete.
        using IDbTransaction transaction = conn.BeginTransaction();

        await conn.InsertAsync(entity, transaction);

        foreach (NotificationTranslation translation in translations ?? [])
        {
            // The original language never becomes a translation row — the read path COALESCEs the
            // translation over the notification's own text, so a row for the original language would
            // be a second copy of text that is already there, free to drift from it.
            if (string.Equals(translation.Language, entity.OriginalLanguage, StringComparison.OrdinalIgnoreCase))
                continue;

            await conn.InsertAsync(
                new NotificationTranslationEntity
                {
                    NotificationId = entity.Id,
                    Language       = translation.Language,
                    Title          = translation.Title,
                    Body           = translation.Body,
                },
                transaction);
        }

        transaction.Commit();
        return entity;
    }

    /// <inheritdoc/>
    public async Task<NotificationEntity?> DismissAsync(Guid id, string? language = null)
    {
        // The read half goes through JoinQueryRepository/IJoinStrategy per ADR 017 — since #319
        // SelectById is a two-table projection, so it is the same join the reader executes and cannot
        // stay a hand-rolled query here. The update half below is a single-table UPDATE and is not in
        // scope for that ADR.
        JoinQueryRepository<NotificationEntity> byId = new(Factory, new NotificationJoinStrategies.ById());
        IReadOnlyList<NotificationEntity> found = await byId.QueryAsync(new { id, lang = language });
        NotificationEntity? entity = found.Count > 0 ? found[0] : null;
        if (entity is null)
            return null;

        using IDbConnection conn = Factory.CreateConnection();
        conn.Open();

        SafeValue<DateTime?> now = SafeDateValue.Now;
        await conn.ExecuteAsync(Sql.Notifications.UpdateDismissById,
            new { id, dismissedAt = now.Raw, dateModified = now.Raw });

        entity.IsDismissed = true;
        entity.DismissedAt = now;
        return entity;
    }

    /// <inheritdoc/>
    public async Task<int> DismissByTriggerAsync(NotificationDismissTrigger trigger)
    {
        using IDbConnection conn = Factory.CreateConnection();
        conn.Open();

        SafeValue<DateTime?> now = SafeDateValue.Now;
        return await conn.ExecuteAsync(Sql.Notifications.UpdateDismissByTrigger,
            new { trigger = trigger.ToString(), dismissedAt = now.Raw, dateModified = now.Raw });
    }
}

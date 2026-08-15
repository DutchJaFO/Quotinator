using System.Text.Json;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Notifications;

/// <summary>
/// Writes a notification exactly once across the lifetime of the database, however many times the app
/// restarts, identified structurally by its metadata payload (#312).
/// <para>
/// Lives in <c>Quotinator.Data</c> rather than <c>Quotinator.Api.Startup</c>, where #279 first put it.
/// Notifications are system content, which ADR 018 places on this side; the practical driver is that
/// <c>Quotinator.Core</c>'s own producers cannot reach into <c>Quotinator.Api</c> (the dependency runs
/// Api → Core, never the reverse), so a helper stranded in the Api layer would have had to be
/// reimplemented for every Core-side producer. #302, #303 and #304 each deferred that decision to
/// their own planning phase; this settles it once for all three.
/// </para>
/// </summary>
public static class NotificationSeeding
{
    /// <summary>
    /// Writes a notification unless one identifying the same thing already exists anywhere in the full
    /// history — active, expired, or dismissed. Returns the newly written entity, or
    /// <see langword="null"/> when an existing notification suppressed the write.
    /// </summary>
    /// <param name="reader">Supplies the existing history the comparison runs against.</param>
    /// <param name="writer">Performs the write when nothing matches.</param>
    /// <param name="type">Severity/kind of the notification.</param>
    /// <param name="metadata">
    /// The producer's payload, which both identifies the notification and is stored alongside it. Its
    /// runtime type is what gets serialized, so a derived type's own properties are preserved.
    /// </param>
    /// <param name="body">The message text.</param>
    /// <param name="title">Optional short headline shown above <paramref name="body"/>.</param>
    /// <param name="dismissTrigger">Which action, if performed, supersedes this notification.</param>
    /// <param name="appVersionId">The <c>System_AppVersion</c> row for the version adding this notification.</param>
    /// <param name="expiresAt">When this notification stops being active. <see langword="null"/> means it never expires (#312's opt-in expiry).</param>
    public static async Task<NotificationEntity?> SeedOnceAsync(
        INotificationReader reader,
        INotificationWriter writer,
        NotificationType type,
        NotificationMetadataDto metadata,
        string body,
        string? title = null,
        NotificationDismissTrigger? dismissTrigger = null,
        Guid? appVersionId = null,
        DateTime? expiresAt = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        // pageSize 0 is this project's "every matching row as a single page" contract, not an empty
        // page — the check has to see dismissed and expired rows too, or a dismissed notification
        // would be rewritten on the next restart.
        PagedItems<NotificationEntity> history = await reader.GetPagedAsync(1, 0);
        if (history.Items.Any(stored => IdentifiesSameNotification(stored, metadata)))
            return null;

        // Serialized against the runtime type, never the declared one: JsonSerializer only emits the
        // properties of the type it is told about, so passing NotificationMetadataDto here would
        // silently drop every field a producer's derived type added and store an empty payload.
        string metadataJson = JsonSerializer.Serialize(metadata, metadata.GetType());

        return await writer.WriteAsync(
            type, body, title,
            expiresAt:      expiresAt,
            dismissTrigger: dismissTrigger,
            metadata:       metadataJson,
            metadataKind:   metadata.Kind,
            appVersionId:   appVersionId);
    }

    /// <summary>
    /// Whether an already-stored notification identifies the same thing as <paramref name="candidate"/>.
    /// The row's own <c>MetadataKind</c> column selects the type to read its payload back as, so this
    /// needs no knowledge of which producer wrote it.
    /// </summary>
    private static bool IdentifiesSameNotification(NotificationEntity stored, NotificationMetadataDto candidate)
    {
        // A row with no readable payload identifies nothing, so it suppresses nothing. That covers
        // every row written before #312 (null Metadata and MetadataKind alike) as well as any whose
        // shape a later version can no longer read — neither is an error, and neither should stop the
        // rest of the history being evaluated.
        NotificationMetadataDto? storedMetadata =
            NotificationMetadataKinds.TryDeserialize(stored.MetadataKind.Parsed, stored.Metadata);

        return storedMetadata is not null && candidate.IsSameNotificationAs(storedMetadata);
    }
}

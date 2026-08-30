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
    /// <param name="appVersionId">
    /// The <c>System_AppVersion</c> row for the version adding this notification, or
    /// <see langword="null"/> when it could not be determined. No default — see
    /// <see cref="INotificationWriter.WriteAsync"/> for why provenance has to be stated rather than
    /// omitted.
    /// </param>
    /// <param name="title">Optional short headline shown above <paramref name="body"/>.</param>
    /// <param name="dismissTrigger">Which action, if performed, supersedes this notification.</param>
    /// <param name="expiresAt">When this notification stops being active. <see langword="null"/> means it never expires (#312's opt-in expiry).</param>
    /// <param name="translations">Every non-original language's title and body, passed through to <see cref="INotificationWriter.WriteAsync"/> (#319). Never part of the identity comparison.</param>
    public static async Task<NotificationEntity?> SeedOnceAsync(
        INotificationReader reader,
        INotificationWriter writer,
        NotificationType type,
        NotificationMetadataDto metadata,
        string body,
        Guid? appVersionId,
        string? title = null,
        NotificationDismissTrigger? dismissTrigger = null,
        DateTime? expiresAt = null,
        IReadOnlyList<NotificationTranslation>? translations = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        // pageSize 0 is this project's "every matching row as a single page" contract, not an empty
        // page — the check has to see dismissed and expired rows too, or a dismissed notification
        // would be rewritten on the next restart.
        PagedItems<NotificationEntity> history = await reader.GetPagedAsync(1, 0);

        return await WriteUnlessAlreadyPresentAsync(
            history.Items, writer, type, metadata, body, appVersionId, title,
            dismissTrigger, expiresAt, translations);
    }

    /// <summary>
    /// Writes a notification unless one identifying the same thing is still <b>active</b> — undismissed,
    /// unexpired and not soft-deleted. Returns the newly written entity, or <see langword="null"/> when
    /// an active notification suppressed the write.
    /// <para>
    /// The sibling of <see cref="SeedOnceAsync"/>, for a producer describing a <b>condition that can
    /// recur</b> rather than an event that happened once (#304). Dedupe here means "while unresolved":
    /// a recommendation stops suppressing the moment it is dismissed, so the same condition arising
    /// again notifies again. That makes dismissal load-bearing — whatever resolves the condition must
    /// actually dismiss, or the notification stays active forever and silently swallows every later
    /// occurrence.
    /// </para>
    /// <para>
    /// Do not "simplify" the two into one helper with a flag. <see cref="SeedOnceAsync"/>'s
    /// full-history comparison is deliberate and load-bearing for #279, #289 and #81 — each describes
    /// something that happened once, and narrowing it to active rows would make all three re-announce
    /// themselves after a user dismissed one and restarted.
    /// </para>
    /// </summary>
    /// <param name="reader">Supplies the active notifications the comparison runs against.</param>
    /// <param name="writer">Performs the write when nothing matches.</param>
    /// <param name="type">Severity/kind of the notification.</param>
    /// <param name="metadata">
    /// The producer's payload, which both identifies the notification and is stored alongside it. Its
    /// runtime type is what gets serialized, so a derived type's own properties are preserved.
    /// </param>
    /// <param name="body">The message text.</param>
    /// <param name="appVersionId">
    /// The <c>System_AppVersion</c> row for the version adding this notification, or
    /// <see langword="null"/> when it could not be determined.
    /// </param>
    /// <param name="title">Optional short headline shown above <paramref name="body"/>.</param>
    /// <param name="dismissTrigger">Which action, if performed, supersedes this notification.</param>
    /// <param name="expiresAt">When this notification stops being active. <see langword="null"/> means it never expires.</param>
    /// <param name="translations">Every non-original language's title and body, passed through to <see cref="INotificationWriter.WriteAsync"/> (#319). Never part of the identity comparison.</param>
    public static async Task<NotificationEntity?> SeedWhileUnresolvedAsync(
        INotificationReader reader,
        INotificationWriter writer,
        NotificationType type,
        NotificationMetadataDto metadata,
        string body,
        Guid? appVersionId,
        string? title = null,
        NotificationDismissTrigger? dismissTrigger = null,
        DateTime? expiresAt = null,
        IReadOnlyList<NotificationTranslation>? translations = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        // No language: the comparison reads the stored Metadata payload, never the resolved text, so
        // which language the reader would render is irrelevant here.
        IReadOnlyList<NotificationEntity> active = await reader.GetActiveNotificationsAsync();

        return await WriteUnlessAlreadyPresentAsync(
            active, writer, type, metadata, body, appVersionId, title,
            dismissTrigger, expiresAt, translations);
    }

    /// <summary>
    /// The half both helpers share: compare <paramref name="metadata"/> against
    /// <paramref name="existing"/> and write when nothing matches. Only the set of rows differs between
    /// the two, so the comparison and the write live here once — otherwise the two could drift apart in
    /// how they read a stored payload back, which is exactly the bug neither would show in its own test.
    /// </summary>
    private static async Task<NotificationEntity?> WriteUnlessAlreadyPresentAsync(
        IReadOnlyList<NotificationEntity> existing,
        INotificationWriter writer,
        NotificationType type,
        NotificationMetadataDto metadata,
        string body,
        Guid? appVersionId,
        string? title,
        NotificationDismissTrigger? dismissTrigger,
        DateTime? expiresAt,
        IReadOnlyList<NotificationTranslation>? translations)
    {
        if (existing.Any(stored => IdentifiesSameNotification(stored, metadata)))
            return null;

        // Through the registry that also reads it back, so the write and read halves of the round-trip
        // cannot drift apart in how they treat runtime types or unset properties.
        string metadataJson = NotificationMetadataKinds.Serialize(metadata);

        return await writer.WriteAsync(
            type, body, appVersionId, title,
            expiresAt:      expiresAt,
            dismissTrigger: dismissTrigger,
            metadata:       metadataJson,
            metadataKind:   metadata.Kind,
            translations:   translations);
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

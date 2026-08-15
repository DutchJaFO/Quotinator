using System.Text.Json.Serialization;
using Quotinator.Data.Enums;

namespace Quotinator.Data.Notifications;

/// <summary>
/// Payload for a <see cref="NotificationMetadataKind.Announcement"/> notification (#279, restructured
/// by #312) — a one-off message announced once and never repeated.
/// </summary>
public sealed class AnnouncementMetadataDto() : NotificationMetadataDto(NotificationMetadataKind.Announcement)
{
    /// <summary>
    /// Stable name for the specific announcement, e.g. <c>GetAllImportBatches</c> for #279's
    /// operation-id renames. A plain name, not a composed key — <see cref="NotificationMetadataDto.Kind"/>
    /// already distinguishes announcements from every other notification shape, so this only has to be
    /// unique among announcements.
    /// </summary>
    [JsonPropertyName("announcement")]
    public required string Announcement { get; init; }

    /// <inheritdoc/>
    protected override IEnumerable<object?> IdentityComponents => [Announcement];
}

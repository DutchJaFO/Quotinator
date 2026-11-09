using System.Text.Json.Serialization;
using Quotinator.Data.Enums;

namespace Quotinator.Data.Notifications;

/// <summary>
/// Payload for a <see cref="NotificationMetadataKind.UnreadableDatabaseSetAside"/> notification (#482):
/// which file a Reset set aside, and where.
/// <para>
/// Facts and identity, not prose, per ADR 026 rule 6: the name is here so a consumer can find the file,
/// and so #481 can offer to download or remove it from the notification.
/// </para>
/// </summary>
public sealed class UnreadableDatabaseSetAsideMetadataDto()
    : NotificationMetadataDto(NotificationMetadataKind.UnreadableDatabaseSetAside)
{
    /// <summary>The set-aside file's name inside <see cref="Folder"/>.</summary>
    [JsonPropertyName("fileName")]
    public required string FileName { get; init; }

    /// <summary>The reserved folder's name inside the backups folder.</summary>
    [JsonPropertyName("folder")]
    public required string Folder { get; init; }

    /// <summary>
    /// The file name: every Reset that sets a file aside gives it a name no other set-aside file has, so
    /// two such notifications are never the same one.
    /// </summary>
    protected override IEnumerable<object?> IdentityComponents => [FileName];
}

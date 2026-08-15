using System.Text.Json;
using Quotinator.Data.Enums;

namespace Quotinator.Data.Notifications;

/// <summary>
/// Maps each <see cref="NotificationMetadataKind"/> to the payload type that shape deserializes into
/// (#312) — the mechanism that makes the round-trip through the <c>Metadata</c> column trivial in both
/// directions.
/// <para>
/// This is the whole reason <c>MetadataKind</c> is a column of its own rather than something inferred:
/// writing serializes a producer's own type, and reading a row hands back that same type without the
/// reader needing to know which producer wrote it or attempting to sniff the JSON's shape. A discriminator
/// stored alongside the payload turns "deserialize arbitrary stored JSON" into a dictionary lookup.
/// </para>
/// </summary>
public static class NotificationMetadataKinds
{
    // One entry per enum member. NotificationMetadataKindsTests asserts that is still true, so adding
    // a kind without its payload type fails a test rather than silently deserializing to nothing at
    // runtime — the same "guard it mechanically rather than remember it" approach ADR 008's CHECK
    // constraints take for the storage side of the same enum.
    private static readonly Dictionary<NotificationMetadataKind, Type> PayloadTypes = new()
    {
        [NotificationMetadataKind.Announcement]          = typeof(AnnouncementMetadataDto),
        [NotificationMetadataKind.SchemaVersionOvershoot] = typeof(SchemaVersionOvershootMetadataDto),
        [NotificationMetadataKind.WhatsNew]               = typeof(WhatsNewMetadataDto),
    };

    /// <summary>The payload type <paramref name="kind"/> deserializes into.</summary>
    /// <param name="kind">The kind recorded on the row.</param>
    public static Type PayloadTypeFor(NotificationMetadataKind kind) => PayloadTypes[kind];

    /// <summary>Every kind that has a registered payload type — the enumeration the guard test checks against.</summary>
    public static IReadOnlyCollection<NotificationMetadataKind> RegisteredKinds => PayloadTypes.Keys;

    /// <summary>
    /// Deserializes a stored <c>Metadata</c> value into the payload type <paramref name="kind"/> names,
    /// or <see langword="null"/> when it cannot be read as that shape.
    /// <para>
    /// Returning null rather than throwing is deliberate and load-bearing: this runs over the whole
    /// notification history on every startup, and one unreadable historical row — written by a version
    /// whose payload shape has since changed, say — must not stop every later notification from being
    /// evaluated. An unreadable row simply cannot be identified, so it identifies nothing.
    /// </para>
    /// </summary>
    /// <param name="kind">The kind recorded on the row, or <see langword="null"/> for a pre-#312 row.</param>
    /// <param name="metadataJson">The row's stored <c>Metadata</c> text.</param>
    public static NotificationMetadataDto? TryDeserialize(NotificationMetadataKind? kind, string? metadataJson)
    {
        if (kind is null || string.IsNullOrWhiteSpace(metadataJson))
            return null;

        if (!PayloadTypes.TryGetValue(kind.Value, out Type? payloadType))
            return null;

        try
        {
            return JsonSerializer.Deserialize(metadataJson, payloadType) as NotificationMetadataDto;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

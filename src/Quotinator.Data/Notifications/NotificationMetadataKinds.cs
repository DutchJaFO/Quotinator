using System.Text.Json;
using System.Text.Json.Serialization;
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
        [NotificationMetadataKind.ReseedRecommended]      = typeof(ReseedRecommendedMetadataDto),
        [NotificationMetadataKind.ReseedFileApplied]      = typeof(ReseedFileAppliedMetadataDto),
        [NotificationMetadataKind.ImportReviewPending]    = typeof(ImportReviewPendingMetadataDto),
    };

    // A null-valued property states nothing and leaves the reader to decide what it was supposed to
    // mean, which is the same defect as inferring "unreleased" from an absent version — so an unset
    // property is omitted rather than stored. Held here, alongside the deserialization it has to match,
    // rather than as an attribute each payload repeats: a producer cannot forget a rule it never has to
    // apply, exactly as Kind stopped being forgettable once there was no override to declare it on.
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The payload type <paramref name="kind"/> deserializes into.</summary>
    /// <param name="kind">The kind recorded on the row.</param>
    public static Type PayloadTypeFor(NotificationMetadataKind kind) => PayloadTypes[kind];

    /// <summary>
    /// Serializes <paramref name="payload"/> for storage in a notification's <c>Metadata</c> column.
    /// <para>
    /// Always against the runtime type, never the declared one: <c>JsonSerializer</c> emits only the
    /// properties of the type it is told about, so passing the base type would silently store an empty
    /// payload and drop every field the producer actually set.
    /// </para>
    /// </summary>
    /// <param name="payload">The producer's own payload instance.</param>
    public static string Serialize(NotificationMetadataDto payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return JsonSerializer.Serialize(payload, payload.GetType(), SerializerOptions);
    }

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
            return JsonSerializer.Deserialize(metadataJson, payloadType, SerializerOptions) as NotificationMetadataDto;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

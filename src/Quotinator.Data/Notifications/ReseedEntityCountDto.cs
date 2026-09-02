using System.Text.Json.Serialization;

namespace Quotinator.Data.Notifications;

/// <summary>
/// How many rows of one entity type a reseeded file added and modified (#302). One entry per type that
/// actually changed — a type with nothing to report is omitted rather than carried as a pair of zeros,
/// matching <see cref="NotificationMetadataDto"/>'s rule that a payload states what it means and omits
/// what it does not.
/// </summary>
/// <remarks>
/// <see cref="EntityType"/> is the same free-text discriminator <c>Import_Action.EntityType</c> carries,
/// so a consumer can line a count up against the actions that produced it. Its values come from
/// <c>ImportActionEntityTypes</c> in <c>Quotinator.Core</c>, which this project cannot reference (ADR
/// 004) — hence a string here rather than an enum, exactly as the <c>Import_Action</c> column itself
/// does it.
/// </remarks>
public sealed class ReseedEntityCountDto
{
    /// <summary>Which entity type these counts describe, e.g. <c>Quote</c> or <c>Source</c>.</summary>
    [JsonPropertyName("entityType")]
    public required string EntityType { get; init; }

    /// <summary>How many rows of this type the file added.</summary>
    [JsonPropertyName("added")]
    public int Added { get; init; }

    /// <summary>How many rows of this type the file modified.</summary>
    [JsonPropertyName("modified")]
    public int Modified { get; init; }

    /// <summary>
    /// How many of this entity type arrived in the file (#373), whatever became of them.
    /// <para>
    /// Absent from every notification written before #373, so it deserialises to <c>0</c> for those —
    /// which is why it is a plain <c>int</c> rather than something that could distinguish "none" from
    /// "not recorded". Those rows predate the distinction entirely.
    /// </para>
    /// </summary>
    [JsonPropertyName("incoming")]
    public int Incoming { get; init; }

    /// <summary>How many arrived and were already exactly as stored (#373).</summary>
    [JsonPropertyName("unchanged")]
    public int Unchanged { get; init; }

    /// <summary>
    /// How many were held because the stored row is marked Complete and the import would change it
    /// (#373).
    /// </summary>
    [JsonPropertyName("blocked")]
    public int Blocked { get; init; }

    /// <summary>How many are waiting on a decision (#373).</summary>
    [JsonPropertyName("pending")]
    public int Pending { get; init; }

    /// <summary>How many were thrown away rather than applied (#373).</summary>
    [JsonPropertyName("discarded")]
    public int Discarded { get; init; }

    /// <summary>
    /// How many matched a per-source rule whose recorded snapshot no longer describes the current
    /// values (#373).
    /// </summary>
    [JsonPropertyName("stale")]
    public int Stale { get; init; }
}

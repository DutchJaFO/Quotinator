using System.Text.Json.Serialization;

namespace Quotinator.Data.Import;

/// <summary>
/// A hand-authored, one-way substitution from a known-wrong raw <c>(title, type)</c> pair (as a bundled
/// source file actually spells it) to the canonical pair an already-established Source uses. Unlike
/// <see cref="ConflictResolutionRule"/> (keyed by entity id, consulted only when that id already exists),
/// this is keyed by the raw incoming value itself and is consulted before Source resolution ever runs —
/// so it applies equally to a brand-new quote (never-before-seen id) and to a re-imported one.
/// </summary>
public sealed class SourceAliasRule
{
    /// <summary>The raw title as a bundled source file actually spells it — matched case-insensitively.</summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>The raw type as a bundled source file actually carries it — matched case-insensitively.</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>The correct title to resolve/create the Source under instead.</summary>
    [JsonPropertyName("canonicalTitle")]
    public required string CanonicalTitle { get; init; }

    /// <summary>The correct type to resolve/create the Source under instead.</summary>
    [JsonPropertyName("canonicalType")]
    public required string CanonicalType { get; init; }
}

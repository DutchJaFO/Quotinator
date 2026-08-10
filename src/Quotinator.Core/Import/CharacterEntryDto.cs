using Quotinator.Core.Enums;
using System.Text.Json.Serialization;
using Quotinator.Core.Models;

namespace Quotinator.Core.Import;

/// <summary>
/// A Character declaration deserialized from a Quotinator source file's <c>characters</c> section
/// (#175). Two shapes, distinguished by whether <see cref="Id"/> is present:
/// <list type="bullet">
/// <item><b>Correction</b> (<see cref="Id"/> set) — matched by that explicit id, decoupling matching
/// from content, so <see cref="Name"/> can be corrected. <see cref="SourceTitle"/>/
/// <see cref="SourceType"/> are present but never diffed or written on this path — a Character's
/// <c>SourceType</c> anchor is immutable once set (ADR 013 Decision 9).</item>
/// <item><b>Creation/Enrichment</b> (<see cref="Id"/> omitted) — matched, or if genuinely new
/// created, via ADR 013's Type-anchored, Series-scoped algorithm using <see cref="Name"/> +
/// <see cref="SourceTitle"/> + <see cref="SourceType"/>, the identical identity test
/// <c>ResolveCharacterAsync</c> applies per-quote, just decoupled from any specific quote's own
/// text.</item>
/// </list>
/// </summary>
public sealed class CharacterEntryDto
{
    /// <summary>
    /// Unique identifier (UUID v4). Assigned at authoring time and never changes. Omit it to match
    /// or create via <see cref="SourceTitle"/>/<see cref="SourceType"/>/<see cref="Name"/> instead —
    /// see this class's own remarks for the two shapes.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>The character's name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// The Source this Character is anchored to for ADR 013 matching purposes — same title text a
    /// quote's own <c>source</c> field uses. Ignored (never diffed) when <see cref="Id"/> is present.
    /// </summary>
    [JsonPropertyName("sourceTitle")]
    public required string SourceTitle { get; init; }

    /// <summary>
    /// The Source.Type anchor (ADR 011) for ADR 013 matching. Ignored (never diffed) when
    /// <see cref="Id"/> is present — a Character's Type anchor is immutable once set.
    /// </summary>
    [JsonPropertyName("sourceType")]
    [JsonConverter(typeof(QuoteTypeJsonConverter))]
    public QuoteType SourceType { get; init; } = QuoteType.Movie;
}

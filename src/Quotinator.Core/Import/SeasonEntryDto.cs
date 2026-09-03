using System.Text.Json.Serialization;

namespace Quotinator.Core.Import;

/// <summary>
/// An explicit Season declaration deserialized from a Quotinator source file's <c>seasons</c> section
/// (#375), mirroring <see cref="SeriesEntryDto"/>'s shape: <see cref="Id"/> present → Correction,
/// matched by that id; <see cref="Id"/> absent → Creation/Enrichment, matched or created by
/// (<see cref="SeriesName"/>, <see cref="Number"/>) via <see cref="EntityIdentity.SeasonId"/>.
/// </summary>
public sealed class SeasonEntryDto
{
    /// <summary>Explicit stable id. Present → Correction shape, matched by this id.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>The season's ordinal within its series — its natural key alongside <see cref="SeriesName"/>.</summary>
    [JsonPropertyName("number")]
    public required int Number { get; init; }

    /// <summary>
    /// Name of the Series this Season belongs to. Resolved to a Series id at import time — never a raw
    /// id, same reasoning as <see cref="SourceEntryDto.SeriesName"/>.
    /// </summary>
    [JsonPropertyName("seriesName")]
    public string? SeriesName { get; init; }

    /// <summary>The season's own name, where it has one — "Book One" for Avatar: The Last Airbender's first season.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>The season's subtitle, where it has one — "Water", rendering "Book One: Water".</summary>
    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; init; }
}

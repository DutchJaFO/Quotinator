using System.Text.Json.Serialization;

namespace Quotinator.Core.Import;

/// <summary>
/// An explicit Series declaration deserialized from a Quotinator source file's <c>series</c> section
/// (#180). Widened by #163 to the same two-shape pattern <see cref="SourceEntry"/>/<see cref="PersonEntry"/>
/// use: <see cref="Id"/> present → Correction, matched by that id; <see cref="Id"/> absent →
/// Creation/Enrichment, matched/created by <see cref="Name"/> via <see cref="EntityIdentity.SeriesId"/>.
/// </summary>
public sealed class SeriesEntry
{
    /// <summary>Explicit stable id (#163). Present → Correction shape, matched by this id.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>The series' name. Unique.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Name of the Universe (#180) this Series belongs to, if any. Resolved to a Universe id at
    /// import time — never a raw id, same reasoning as <see cref="SourceEntry.SeriesName"/>.
    /// </summary>
    [JsonPropertyName("universeName")]
    public string? UniverseName { get; init; }
}

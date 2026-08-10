using System.Text.Json.Serialization;

namespace Quotinator.Core.Import;

/// <summary>
/// An explicit Universe declaration deserialized from a Quotinator source file's <c>universe</c>
/// section (#180). Widened by #163 to the same two-shape pattern <see cref="SourceEntryDto"/>/
/// <see cref="PersonEntryDto"/> use: <see cref="Id"/> present → Correction, matched by that id;
/// <see cref="Id"/> absent → Creation/Enrichment, matched/created by <see cref="Name"/> via
/// <see cref="EntityIdentity.UniverseId"/>.
/// </summary>
public sealed class UniverseEntryDto
{
    /// <summary>Explicit stable id (#163). Present → Correction shape, matched by this id.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>The universe's name. Unique.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

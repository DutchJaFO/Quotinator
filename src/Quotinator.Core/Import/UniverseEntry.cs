using System.Text.Json.Serialization;

namespace Quotinator.Core.Import;

/// <summary>
/// An explicit Universe declaration deserialized from a Quotinator source file's <c>universe</c>
/// section (#180). Matched by <see cref="Name"/> only — unlike <see cref="SourceEntry"/>/<see cref="PersonEntry"/>,
/// a Universe entry carries no explicit <c>id</c>, since <see cref="EntityIdentity.UniverseId"/>
/// derives it from the name alone.
/// </summary>
public sealed class UniverseEntry
{
    /// <summary>The universe's name. Unique.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

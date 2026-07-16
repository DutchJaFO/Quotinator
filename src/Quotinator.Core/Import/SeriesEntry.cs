using System.Text.Json.Serialization;

namespace Quotinator.Core.Import;

/// <summary>
/// An explicit Series declaration deserialized from a Quotinator source file's <c>series</c> section
/// (#180). Matched by <see cref="Name"/> only — unlike <see cref="SourceEntry"/>/<see cref="PersonEntry"/>,
/// a Series entry carries no explicit <c>id</c>, since <see cref="EntityIdentity.SeriesId"/> derives it
/// from the name alone.
/// </summary>
public sealed class SeriesEntry
{
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

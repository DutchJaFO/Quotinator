using System.Text.Json.Serialization;

namespace Quotinator.Core.Import;

/// <summary>
/// An explicit Person declaration deserialized from a Quotinator source file's <c>people</c> section
/// (#173). Decouples matching from content — a Person found by <see cref="Id"/> can have its
/// <see cref="Name"/>/<see cref="DateOfBirth"/>/<see cref="DateOfDeath"/> corrected, unlike a Person
/// only ever discovered implicitly through a quote's own author string, which is matched by natural key.
/// </summary>
public sealed class PersonEntry
{
    /// <summary>Unique identifier (UUID v4). Assigned at authoring time and never changes.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The person's full name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Imprecise ISO 8601 birth date (e.g. "1955" or "1955-02-24"). Null when unknown.</summary>
    [JsonPropertyName("dateOfBirth")]
    public string? DateOfBirth { get; init; }

    /// <summary>Imprecise ISO 8601 death date. Null when still living or unknown.</summary>
    [JsonPropertyName("dateOfDeath")]
    public string? DateOfDeath { get; init; }
}

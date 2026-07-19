using System.Text.Json.Serialization;
using Quotinator.Data.Import;

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

    /// <summary>
    /// Imprecise ISO 8601 birth date (e.g. "1955" or "1955-02-24"). Absent means leave the existing
    /// value alone; present with <c>null</c> means reset it (#190).
    /// </summary>
    [JsonPropertyName("dateOfBirth")]
    public Optional<string> DateOfBirth { get; init; }

    /// <summary>
    /// Imprecise ISO 8601 death date. Absent means leave the existing value alone; present with
    /// <c>null</c> means reset it (#190).
    /// </summary>
    [JsonPropertyName("dateOfDeath")]
    public Optional<string> DateOfDeath { get; init; }
}

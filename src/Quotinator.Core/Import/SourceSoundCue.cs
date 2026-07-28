using System.Text.Json.Serialization;
using Quotinator.Data.Import;

namespace Quotinator.Core.Import;

/// <summary>A reusable audio element entry deserialized from a Quotinator source file's <c>soundCues</c> section.</summary>
public sealed class SourceSoundCue
{
    /// <summary>Unique identifier (UUID v4). Assigned at authoring time and never changes.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The sound cue text in its original language.</summary>
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    /// <summary>
    /// Optional audio file for the cue. Absent means leave the existing value alone; present with
    /// <c>null</c> means reset it (#190).
    /// </summary>
    [JsonPropertyName("soundFileUrl")]
    public Optional<string> SoundFileUrl { get; init; }

    /// <summary>
    /// Optional image illustrating the cue. Absent means leave the existing value alone; present with
    /// <c>null</c> means reset it (#190).
    /// </summary>
    [JsonPropertyName("imageUrl")]
    public Optional<string> ImageUrl { get; init; }

    /// <summary>Available translations of <see cref="Text"/>, keyed by ISO 639-1 language code.</summary>
    [JsonPropertyName("translations")]
    public IReadOnlyDictionary<string, SourceSoundCueTranslation> Translations { get; init; }
        = new Dictionary<string, SourceSoundCueTranslation>();
}

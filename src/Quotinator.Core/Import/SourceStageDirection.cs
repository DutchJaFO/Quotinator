using System.Text.Json.Serialization;

namespace Quotinator.Core.Import;

/// <summary>A reusable scene-setting or action description entry deserialized from a Quotinator source file's <c>stageDirections</c> section.</summary>
public sealed class SourceStageDirection
{
    /// <summary>Unique identifier (UUID v4). Assigned at authoring time and never changes.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The stage direction text in its original language.</summary>
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    /// <summary>Optional image (e.g. a production still) illustrating the scene.</summary>
    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; init; }

    /// <summary>Available translations of <see cref="Text"/>, keyed by ISO 639-1 language code.</summary>
    [JsonPropertyName("translations")]
    public IReadOnlyDictionary<string, SourceStageDirectionTranslation> Translations { get; init; }
        = new Dictionary<string, SourceStageDirectionTranslation>();
}

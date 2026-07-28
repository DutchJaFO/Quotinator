using System.Text.Json.Serialization;

namespace Quotinator.Core.Import;

/// <summary>A translated version of a <see cref="SourceStageDirection"/>'s text for a specific language.</summary>
public sealed class SourceStageDirectionTranslation
{
    /// <summary>The translated text.</summary>
    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

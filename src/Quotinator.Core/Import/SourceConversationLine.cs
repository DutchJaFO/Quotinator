using System.Text.Json.Serialization;
using Quotinator.Core.Models;

namespace Quotinator.Core.Import;

/// <summary>
/// One position in a <see cref="SourceConversation"/>'s ordered line list. Exactly one of
/// <see cref="QuoteId"/>, <see cref="StageDirectionId"/>, <see cref="SoundCueId"/> is populated,
/// matching <see cref="Type"/> — the same discriminated shape as
/// <c>Quotinator.Engine.Entities.ConversationLineEntity</c>.
/// </summary>
public sealed class SourceConversationLine
{
    /// <summary>1-based position of this line within the conversation.</summary>
    [JsonPropertyName("order")]
    public required int Order { get; init; }

    /// <summary>Which of <see cref="QuoteId"/>/<see cref="StageDirectionId"/>/<see cref="SoundCueId"/> is populated.</summary>
    [JsonPropertyName("type")]
    [JsonConverter(typeof(ConversationLineTypeJsonConverter))]
    public required ConversationLineType Type { get; init; }

    /// <summary>Populated when <see cref="Type"/> is <see cref="ConversationLineType.Quote"/>.</summary>
    [JsonPropertyName("quoteId")]
    public string? QuoteId { get; init; }

    /// <summary>Populated when <see cref="Type"/> is <see cref="ConversationLineType.StageDirection"/>.</summary>
    [JsonPropertyName("stageDirectionId")]
    public string? StageDirectionId { get; init; }

    /// <summary>Populated when <see cref="Type"/> is <see cref="ConversationLineType.SoundCue"/>.</summary>
    [JsonPropertyName("soundCueId")]
    public string? SoundCueId { get; init; }
}

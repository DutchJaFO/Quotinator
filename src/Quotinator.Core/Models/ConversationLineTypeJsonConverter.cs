using System.Text.Json;
using System.Text.Json.Serialization;

namespace Quotinator.Core.Models;

/// <summary>
/// Serializes <see cref="ConversationLineType"/> using snake_case wire values (<c>stage_direction</c>,
/// <c>sound_cue</c>), matching <c>schemas/source-extended.schema.json</c>'s <c>conversation.lines[].type</c>
/// enum. A parameterless-constructor subclass is required because <see cref="JsonConverterAttribute"/>
/// can only invoke a converter's parameterless constructor.
/// </summary>
public sealed class ConversationLineTypeJsonConverter : JsonStringEnumConverter<ConversationLineType>
{
    /// <summary>Initializes the converter with snake_case naming.</summary>
    public ConversationLineTypeJsonConverter() : base(JsonNamingPolicy.SnakeCaseLower) { }
}

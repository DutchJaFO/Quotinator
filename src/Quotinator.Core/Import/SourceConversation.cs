using System.Text.Json.Serialization;
using Quotinator.Data.Import;

namespace Quotinator.Core.Import;

/// <summary>An ordered grouping of quotes, stage directions, and sound cues deserialized from a Quotinator source file's <c>conversations</c> section.</summary>
public sealed class SourceConversation
{
    /// <summary>Unique identifier (UUID v4). Assigned at authoring time and never changes.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Optional human-readable label for the conversation. Absent means leave the existing value
    /// alone; present with <c>null</c> means reset it (#190).
    /// </summary>
    [JsonPropertyName("description")]
    public Optional<string> Description { get; init; }

    /// <summary>The conversation's lines, in <see cref="SourceConversationLine.Order"/> order.</summary>
    [JsonPropertyName("lines")]
    public required IReadOnlyList<SourceConversationLine> Lines { get; init; }
}

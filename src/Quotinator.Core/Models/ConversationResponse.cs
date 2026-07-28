namespace Quotinator.Core.Models;

/// <summary>The full ordered line list of a conversation — returned by <c>GET /api/v1/conversations/{id}</c> and embedded in a <c>/random</c> result when a selected quote belongs to one.</summary>
public sealed class ConversationResponse
{
    /// <summary>Unique identifier (UUID v4).</summary>
    public required string Id { get; init; }

    /// <summary>Optional human-readable label for the conversation.</summary>
    public string? Description { get; init; }

    /// <summary>The conversation's lines, in order.</summary>
    public required IReadOnlyList<ConversationLineResponse> Lines { get; init; }
}

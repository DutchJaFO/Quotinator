namespace Quotinator.Core.Models;

/// <summary>Lightweight reference to one conversation a quote appears in — id, position, and line count only, not the full line list.</summary>
public sealed class QuoteConversationMembership
{
    /// <summary>Unique identifier (UUID v4) of the conversation.</summary>
    public required string ConversationId { get; init; }

    /// <summary>1-based position of this quote within the conversation's line list.</summary>
    public required int Position { get; init; }

    /// <summary>Total number of lines in the conversation.</summary>
    public required int TotalLines { get; init; }
}

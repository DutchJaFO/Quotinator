namespace Quotinator.Core.Models;

/// <summary>
/// The API response shape for a single quote.
/// Reflects the requested language: <see cref="Quote"/> and <see cref="Source"/> are in
/// the language identified by <see cref="Language"/>. Compare <see cref="Language"/> with
/// <see cref="OriginalLanguage"/> (or check <see cref="IsTranslated"/>) to determine whether
/// the text is a translation or the original.
/// </summary>
public sealed class QuoteResponse
{
    /// <summary>Unique identifier (UUID v4).</summary>
    public required string Id { get; init; }

    /// <summary>The quote text in the language identified by <see cref="Language"/>.</summary>
    public required string Quote { get; init; }

    /// <summary>ISO 639-1 code of the language in which <see cref="Quote"/> and <see cref="Source"/> are returned.</summary>
    public required string Language { get; init; }

    /// <summary>ISO 639-1 code of the language in which the quote was originally recorded.</summary>
    public required string OriginalLanguage { get; init; }

    /// <summary><c>true</c> when the returned text is a translation rather than the original.</summary>
    public bool IsTranslated => Language != OriginalLanguage;

    /// <summary>The source title in the language identified by <see cref="Language"/>.</summary>
    public required string Source { get; init; }

    /// <summary>
    /// Date associated with the source, in ISO 8601 format (as precise as the source allows).
    /// Examples: <c>"1940-06-04"</c>, <c>"1940-06"</c>, <c>"1994"</c>.
    /// </summary>
    public string? Date { get; init; }

    /// <summary>Fictional character who delivers the quote. Present for <c>movie</c>, <c>tv</c>, <c>anime</c>, and fiction <c>book</c> entries.</summary>
    public string? Character { get; init; }

    /// <summary>Real person associated with the quote — book author or person type attributee.</summary>
    public string? Author { get; init; }

    /// <summary>Source type: <c>movie</c>, <c>tv</c>, <c>anime</c>, <c>book</c>, or <c>person</c>.</summary>
    public required string Type { get; init; }

    /// <summary>Genre tags. See <c>Quote.Genres</c> for standard values.</summary>
    public IReadOnlyList<string> Genres { get; init; } = [];

    /// <summary>
    /// Conversations this quote appears in — id, position, and total line count only, not the full
    /// line list (fetch that via <c>GET /api/v1/conversations/{id}</c>). <c>null</c> when the quote
    /// belongs to no conversation; an empty array is never returned. Always <c>null</c> on a
    /// <see cref="ConversationLineResponse.Quote"/> embedded inside a conversation's own line list —
    /// no recursive expansion.
    /// </summary>
    public IReadOnlyList<QuoteConversationMembership>? Conversations { get; init; }

    /// <summary>
    /// Populated only by <c>GET /api/v1/quotes/random</c>: when this quote belongs to one or more
    /// conversations, one is chosen at random and its full line list embedded here, saving the
    /// caller a second round-trip to <c>GET /api/v1/conversations/{id}</c>. <c>null</c> everywhere
    /// else, and <c>null</c> here too when the quote belongs to no conversation.
    /// </summary>
    public ConversationResponse? EmbeddedConversation { get; init; }
}

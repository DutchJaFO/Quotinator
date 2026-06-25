using System.Text.Json.Serialization;

namespace Quotinator.Data.Import;

/// <summary>Represents a single quote entry deserialized from a Quotinator source file (<c>data/sources/*.json</c>).</summary>
public class SourceQuote
{
    /// <summary>Unique identifier (UUID v4). Assigned at seed time and never changes.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The verbatim quote text in the original language.</summary>
    [JsonPropertyName("quote")]
    public required string QuoteText { get; init; }

    /// <summary>ISO 639-1 language code of the original quote (e.g. <c>"en"</c>, <c>"fr"</c>). Defaults to <c>"en"</c>.</summary>
    [JsonPropertyName("originalLanguage")]
    public string OriginalLanguage { get; init; } = "en";

    /// <summary>The source title — film name, TV series, book title, or speech occasion.</summary>
    [JsonPropertyName("source")]
    public required string Source { get; init; }

    /// <summary>
    /// Date associated with the source, in ISO 8601 format, as precise as the source allows.
    /// May be a full date (<c>"1940-06-04"</c>), year-month (<c>"1940-06"</c>), or year only (<c>"1994"</c>).
    /// </summary>
    [JsonPropertyName("date")]
    public string? Date { get; init; }

    /// <summary>Fictional character who delivers the quote. Applies to <c>movie</c>, <c>tv</c>, <c>anime</c>, and fiction <c>book</c> entries.</summary>
    [JsonPropertyName("character")]
    public string? Character { get; init; }

    /// <summary>Real person associated with the quote — the book's author, or the person who said it (<c>person</c> type).</summary>
    [JsonPropertyName("author")]
    public string? Author { get; init; }

    /// <summary>
    /// Media or source type.
    /// Standard values: <c>movie</c>, <c>tv</c>, <c>anime</c>, <c>book</c>, <c>person</c>.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "movie";

    /// <summary>
    /// Genre tags used for filtering.
    /// Standard values: <c>action</c>, <c>adventure</c>, <c>animation</c>, <c>comedy</c>,
    /// <c>drama</c>, <c>fantasy</c>, <c>fiction</c>, <c>horror</c>, <c>mystery</c>,
    /// <c>non-fiction</c>, <c>romance</c>, <c>sci-fi</c>, <c>thriller</c>.
    /// </summary>
    [JsonPropertyName("genres")]
    public IReadOnlyList<string> Genres { get; init; } = [];

    /// <summary>Available translations of <see cref="QuoteText"/> and <see cref="Source"/>, keyed by ISO 639-1 language code.</summary>
    [JsonPropertyName("translations")]
    public IReadOnlyDictionary<string, SourceQuoteTranslation> Translations { get; init; }
        = new Dictionary<string, SourceQuoteTranslation>();
}

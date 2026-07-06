using System.Text.Json.Serialization;

namespace Quotinator.Core.Models;

/// <summary>
/// The 8 mergeable quote fields (mirrors <c>Quotinator.Engine.Database.QuoteFieldMerge</c>'s field-map
/// shape exactly — <c>Id</c>/translations are deliberately excluded there too). Property names use the
/// same wire tags already stored in <c>System_ImportConflicts.ExistingValue</c>/<c>IncomingValue</c>,
/// so this deserializes directly from that stored JSON with no manual field-by-field mapping.
/// </summary>
public sealed class QuoteConflictFieldsDto
{
    /// <summary>The quote's text.</summary>
    [JsonPropertyName("quoteText")]
    public string? QuoteText { get; init; }

    /// <summary>ISO 639-1 language code the quote is originally in.</summary>
    [JsonPropertyName("originalLanguage")]
    public string? OriginalLanguage { get; init; }

    /// <summary>Film/book/show title or speech occasion.</summary>
    [JsonPropertyName("source")]
    public string? Source { get; init; }

    /// <summary>ISO 8601 date, as precise as the source allows.</summary>
    [JsonPropertyName("date")]
    public string? Date { get; init; }

    /// <summary>Fictional character who said the quote, when applicable.</summary>
    [JsonPropertyName("character")]
    public string? Character { get; init; }

    /// <summary>Real person who said the quote, when applicable.</summary>
    [JsonPropertyName("author")]
    public string? Author { get; init; }

    /// <summary>Quote type (<c>movie</c>, <c>tv</c>, <c>anime</c>, <c>book</c>, <c>person</c>) — wire value.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>Genre tags.</summary>
    [JsonPropertyName("genres")]
    public List<string> Genres { get; init; } = [];
}

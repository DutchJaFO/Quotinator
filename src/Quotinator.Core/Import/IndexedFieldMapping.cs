using System.Text.Json.Serialization;

namespace Quotinator.Core.Import;

/// <summary>
/// Which raw column (CSV) or capture-group (regex) index each canonical quote field is read from.
/// Every slot is optional — an unmapped field falls back to <see cref="QuoteFieldDefaults"/>, then its
/// own built-in default (see <see cref="MappedSourceQuoteBuilder"/>). 1-based: index <c>1</c> is the
/// first column/group.
/// </summary>
public sealed class IndexedFieldMapping
{
    /// <summary>Index the quote's own id is read from. Usually omitted — an id is normally derived via <see cref="QuoteIdentity.StableId"/>.</summary>
    [JsonPropertyName("id")]
    public int? Id { get; init; }

    /// <summary>Index the quote text is read from.</summary>
    [JsonPropertyName("quote")]
    public int? Quote { get; init; }

    /// <summary>Index the original-language code is read from.</summary>
    [JsonPropertyName("originalLanguage")]
    public int? OriginalLanguage { get; init; }

    /// <summary>Index the source title is read from.</summary>
    [JsonPropertyName("source")]
    public int? Source { get; init; }

    /// <summary>Index the date is read from.</summary>
    [JsonPropertyName("date")]
    public int? Date { get; init; }

    /// <summary>Index the character name is read from.</summary>
    [JsonPropertyName("character")]
    public int? Character { get; init; }

    /// <summary>Index the author name is read from.</summary>
    [JsonPropertyName("author")]
    public int? Author { get; init; }

    /// <summary>Index the quote type is read from.</summary>
    [JsonPropertyName("type")]
    public int? Type { get; init; }

    /// <summary>Index the genres value is read from.</summary>
    [JsonPropertyName("genres")]
    public int? Genres { get; init; }
}

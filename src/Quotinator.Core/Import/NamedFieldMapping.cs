using System.Text.Json.Serialization;

namespace Quotinator.Core.Import;

/// <summary>
/// Which raw JSON property name each canonical quote field is read from, for a name-based format (a
/// flat JSON object array). Every slot is optional — an unmapped field falls back to reading the raw
/// object's own property matching the canonical name itself (e.g. <c>Quote</c> unmapped means read the
/// raw <c>"quote"</c> property), then <see cref="QuoteFieldDefaults"/>, then its own built-in default
/// (see <see cref="MappedSourceQuoteBuilder"/>).
/// </summary>
public sealed class NamedFieldMapping
{
    /// <summary>Raw property name the quote's own id is read from. Usually omitted — an id is normally derived via <see cref="QuoteIdentity.StableId"/>.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>Raw property name the quote text is read from.</summary>
    [JsonPropertyName("quote")]
    public string? Quote { get; init; }

    /// <summary>Raw property name the original-language code is read from.</summary>
    [JsonPropertyName("originalLanguage")]
    public string? OriginalLanguage { get; init; }

    /// <summary>Raw property name the source title is read from.</summary>
    [JsonPropertyName("source")]
    public string? Source { get; init; }

    /// <summary>Raw property name the date is read from.</summary>
    [JsonPropertyName("date")]
    public string? Date { get; init; }

    /// <summary>Raw property name the character name is read from.</summary>
    [JsonPropertyName("character")]
    public string? Character { get; init; }

    /// <summary>Raw property name the author name is read from.</summary>
    [JsonPropertyName("author")]
    public string? Author { get; init; }

    /// <summary>Raw property name the quote type is read from.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>Raw property name the genres value is read from.</summary>
    [JsonPropertyName("genres")]
    public string? Genres { get; init; }
}

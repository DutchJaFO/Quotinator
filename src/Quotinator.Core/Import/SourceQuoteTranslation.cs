using System.Text.Json.Serialization;

namespace Quotinator.Core.Import;

/// <summary>A translated version of a <see cref="SourceQuote"/>'s text and source title for a specific language.</summary>
public class SourceQuoteTranslation
{
    /// <summary>The translated quote text.</summary>
    [JsonPropertyName("quote")]
    public required string QuoteText { get; init; }

    /// <summary>The translated source title. May be null when the source title is the same in the target language.</summary>
    [JsonPropertyName("source")]
    public string? Source { get; init; }
}

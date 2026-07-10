using System.Text.Json.Serialization;
using Quotinator.Core.Models;

namespace Quotinator.Core.Import;

/// <summary>
/// Literal default values applied to a canonical quote field not sourced from the raw row at all.
/// <see cref="SourceQuote.QuoteText"/>/<see cref="SourceQuote.Source"/>/<see cref="SourceQuote.Id"/>
/// are deliberately excluded — quote/source are required per row (a row missing either is skipped,
/// never defaulted), and a single fixed id applied to every row would collide.
/// </summary>
public sealed class QuoteFieldDefaults
{
    /// <summary>Default original-language code when no row value is present.</summary>
    [JsonPropertyName("originalLanguage")]
    public string? OriginalLanguage { get; init; }

    /// <summary>Default quote type when no row value is present or recognised.</summary>
    [JsonPropertyName("type")]
    [JsonConverter(typeof(QuoteTypeJsonConverter))]
    public QuoteType? Type { get; init; }

    /// <summary>Default date when no row value is present.</summary>
    [JsonPropertyName("date")]
    public string? Date { get; init; }

    /// <summary>Default character name when no row value is present.</summary>
    [JsonPropertyName("character")]
    public string? Character { get; init; }

    /// <summary>Default author name when no row value is present.</summary>
    [JsonPropertyName("author")]
    public string? Author { get; init; }

    /// <summary>Default genre list when no row value is present.</summary>
    [JsonPropertyName("genres")]
    public IReadOnlyList<string>? Genres { get; init; }
}

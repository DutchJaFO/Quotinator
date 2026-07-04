using System.Text.Json.Serialization;

namespace Quotinator.Converters.NikhilNamal17;

/// <summary>Wire model for a single raw entry in the NikhilNamal17/popular-movie-quotes upstream format.</summary>
internal sealed class NikhilNamal17RawEntry
{
    /// <summary>The verbatim quote text.</summary>
    [JsonPropertyName("quote")]
    public string? Quote { get; init; }

    /// <summary>The film/show title (fieldMap: canonical <c>source</c> ← raw <c>movie</c>).</summary>
    [JsonPropertyName("movie")]
    public string? Movie { get; init; }

    /// <summary>Raw media type, normalised via <see cref="Quotinator.Core.Import.QuoteTypeNormalisation"/>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>Raw release year — inconsistently typed upstream as either a JSON number or string.</summary>
    [JsonPropertyName("year")]
    [JsonConverter(typeof(YearJsonConverter))]
    public string? Year { get; init; }
}

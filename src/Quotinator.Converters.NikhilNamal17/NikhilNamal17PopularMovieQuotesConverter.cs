using System.Text.Json;
using Quotinator.Core.Import;
using Quotinator.Core.Models;
using Quotinator.Data.Import;

namespace Quotinator.Converters.NikhilNamal17;

/// <summary>
/// Converts the NikhilNamal17/popular-movie-quotes source's raw format — a JSON array of objects with
/// <c>quote</c>/<c>movie</c>/<c>type</c>/<c>year</c> fields — into Quotinator's canonical quote schema.
/// </summary>
public sealed class NikhilNamal17PopularMovieQuotesConverter : IQuoteSourceConverter
{
    private const QuoteType DefaultType = QuoteType.Movie;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <inheritdoc/>
    public string Name => "nikhilnamal17";

    /// <inheritdoc/>
    public async Task ConvertAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(inputPath, cancellationToken);

        List<NikhilNamal17RawEntry>? rawEntries;
        try
        {
            rawEntries = JsonSerializer.Deserialize<List<NikhilNamal17RawEntry>>(json);
        }
        catch (JsonException ex)
        {
            throw new SourceConversionException($"nikhilnamal17: input at {inputPath} is not a valid JSON object array", ex);
        }

        if (rawEntries is null || rawEntries.Count == 0)
            throw new SourceConversionException($"nikhilnamal17: input at {inputPath} contained no entries");

        var quotes = new List<SourceQuote>();
        foreach (var entry in rawEntries)
        {
            var quote  = entry.Quote?.Trim();
            var source = entry.Movie?.Trim();
            if (string.IsNullOrWhiteSpace(quote) || string.IsNullOrWhiteSpace(source)) continue;

            quotes.Add(new SourceQuote
            {
                Id               = QuoteIdentity.StableId(quote, source),
                QuoteText        = quote,
                OriginalLanguage = "en",
                Source           = source,
                Date             = YearParsing.CleanYear(entry.Year),
                Character        = null,
                Author           = null,
                Type             = QuoteTypeNormalisation.CanonicalType(entry.Type, DefaultType),
                Genres           = [],
                Translations     = new Dictionary<string, SourceQuoteTranslation>(),
            });
        }

        if (quotes.Count == 0)
            throw new SourceConversionException($"nikhilnamal17: none of the {rawEntries.Count} entries in {inputPath} had both a quote and a source");

        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(quotes, WriteOptions), cancellationToken);
    }
}

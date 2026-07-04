using System.Text.Json;
using System.Text.RegularExpressions;
using Quotinator.Core.Import;
using Quotinator.Data.Import;

namespace Quotinator.Converters.Vilaboim;

/// <summary>
/// Converts the vilaboim/movie-quotes source's raw format — a bare JSON array of strings shaped
/// <c>"Quote text." Source Title</c> — into Quotinator's canonical quote schema.
/// </summary>
public sealed class VilaboimMovieQuotesConverter : IQuoteSourceConverter
{
    private static readonly Regex EntryPattern = new("""^"(.+?)"\s+(.+)$""", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <inheritdoc/>
    public string Name => "vilaboim";

    /// <inheritdoc/>
    public async Task ConvertAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(inputPath, cancellationToken);

        List<string>? rawEntries;
        try
        {
            rawEntries = JsonSerializer.Deserialize<List<string>>(json);
        }
        catch (JsonException ex)
        {
            throw new SourceConversionException($"vilaboim: input at {inputPath} is not a valid JSON string array", ex);
        }

        if (rawEntries is null || rawEntries.Count == 0)
            throw new SourceConversionException($"vilaboim: input at {inputPath} contained no entries");

        var quotes = new List<SourceQuote>();
        foreach (var raw in rawEntries)
        {
            var match = EntryPattern.Match(raw);
            if (!match.Success) continue;

            quotes.Add(new SourceQuote
            {
                Id               = QuoteIdentity.StableId(match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim()),
                QuoteText        = match.Groups[1].Value.Trim(),
                OriginalLanguage = "en",
                Source           = match.Groups[2].Value.Trim(),
                Date             = null,
                Character        = null,
                Author           = null,
                Type             = "movie",
                Genres           = [],
                Translations     = new Dictionary<string, SourceQuoteTranslation>(),
            });
        }

        if (quotes.Count == 0)
            throw new SourceConversionException($"vilaboim: none of the {rawEntries.Count} entries in {inputPath} matched the expected quoted-string format");

        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(quotes, WriteOptions), cancellationToken);
    }
}

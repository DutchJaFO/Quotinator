using System.Text.Json;
using Quotinator.Core.Import;
using Quotinator.Core.Models;
using Quotinator.Data.Import;

namespace Quotinator.Converters.Csv;

/// <summary>
/// Converts a flat CSV rendering of Quotinator's canonical quote schema — header row required
/// (case-insensitive), columns <c>id, quote, originalLanguage, source, date, character, author,
/// type, genres</c> (<c>genres</c> semicolon-delimited within its cell; <c>quote</c>/<c>source</c>
/// are the only required columns) — into Quotinator's canonical JSON schema. A row's own <c>id</c>
/// is used verbatim when present and non-empty; otherwise one is derived deterministically via
/// <see cref="QuoteIdentity.StableId"/>, exactly like the other bundled converters do for sources
/// with no id of their own.
/// </summary>
public sealed class CsvQuoteConverter : IQuoteSourceConverter
{
    private const QuoteType DefaultType = QuoteType.Movie;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <inheritdoc/>
    public string Name => "csv";

    /// <inheritdoc/>
    public async Task ConvertAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default)
    {
        var content = await File.ReadAllTextAsync(inputPath, cancellationToken);
        var rows    = CsvLineParser.Parse(content);

        if (rows.Count < 2)
            throw new SourceConversionException($"csv: input at {inputPath} has no header row or no data rows");

        var header = rows[0]
            .Select((name, index) => (Name: name.Trim(), Index: index))
            .ToDictionary(h => h.Name, h => h.Index, StringComparer.OrdinalIgnoreCase);

        if (!header.ContainsKey("quote") || !header.ContainsKey("source"))
            throw new SourceConversionException($"csv: input at {inputPath} is missing a required 'quote' or 'source' column");

        var quotes = new List<SourceQuote>();
        foreach (var row in rows.Skip(1))
        {
            var quote  = Field(row, header, "quote")?.Trim();
            var source = Field(row, header, "source")?.Trim();
            if (string.IsNullOrWhiteSpace(quote) || string.IsNullOrWhiteSpace(source)) continue;

            var id     = Field(row, header, "id")?.Trim();
            var genres = Field(row, header, "genres")?
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList() ?? [];

            quotes.Add(new SourceQuote
            {
                Id               = string.IsNullOrEmpty(id) ? QuoteIdentity.StableId(quote, source) : id,
                QuoteText        = quote,
                OriginalLanguage = NonEmpty(Field(row, header, "originalLanguage")) ?? "en",
                Source           = source,
                Date             = NonEmpty(Field(row, header, "date")),
                Character        = NonEmpty(Field(row, header, "character")),
                Author           = NonEmpty(Field(row, header, "author")),
                Type             = QuoteTypeNormalisation.CanonicalType(Field(row, header, "type")?.Trim(), DefaultType),
                Genres           = genres,
                Translations     = new Dictionary<string, SourceQuoteTranslation>(),
            });
        }

        if (quotes.Count == 0)
            throw new SourceConversionException($"csv: none of the {rows.Count - 1} row(s) in {inputPath} had both a quote and a source");

        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(quotes, WriteOptions), cancellationToken);
    }

    private static string? Field(List<string> row, Dictionary<string, int> header, string column)
        => header.TryGetValue(column, out var idx) && idx < row.Count ? row[idx] : null;

    private static string? NonEmpty(string? value) => value?.Trim() is { Length: > 0 } trimmed ? trimmed : null;
}

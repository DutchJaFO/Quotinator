using System.Text.Json;
using Quotinator.Core.Import;
using Quotinator.Core.Models;
using Quotinator.Data.Import;

namespace Quotinator.Converters.Csv;

/// <summary>
/// Converts a flat CSV rendering of Quotinator's canonical quote schema into Quotinator's canonical
/// JSON schema. With no <see cref="CsvConverterOptions"/> supplied: header row required
/// (case-insensitive), columns <c>id, quote, originalLanguage, source, date, character, author,
/// type, genres</c> (<c>genres</c> semicolon-delimited within its cell; <c>quote</c>/<c>source</c> are
/// the only required columns). With <see cref="CsvConverterOptions.ColumnMapping"/> supplied, columns
/// are matched by 1-based position instead of header text — see <see cref="CsvConverterOptions"/>. A
/// row's own <c>id</c> is used verbatim when present and non-empty; otherwise one is derived
/// deterministically via <see cref="QuoteIdentity.StableId"/>, exactly like the other bundled
/// converters do for sources with no id of their own.
/// </summary>
public sealed class CsvQuoteConverter : IQuoteSourceConverter
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <inheritdoc/>
    public string Name => "csv";

    /// <inheritdoc/>
    public async Task ConvertAsync(string inputPath, string outputPath, JsonElement? options = null, CancellationToken cancellationToken = default)
    {
        var content     = await File.ReadAllTextAsync(inputPath, cancellationToken);
        var rows        = CsvLineParser.Parse(content);
        var csvOptions  = options?.Deserialize<CsvConverterOptions>() ?? new CsvConverterOptions();
        var mapping     = csvOptions.ColumnMapping;
        var defaults    = csvOptions.Defaults;

        if (rows.Count == 0)
            throw new SourceConversionException($"csv: input at {inputPath} has no rows");

        Dictionary<string, int>? header;
        List<List<string>> dataRows;

        if (mapping is null)
        {
            if (rows.Count < 2)
                throw new SourceConversionException($"csv: input at {inputPath} has no header row or no data rows");

            header = rows[0]
                .Select((name, index) => (Name: name.Trim(), Index: index))
                .ToDictionary(h => h.Name, h => h.Index, StringComparer.OrdinalIgnoreCase);

            if (!header.ContainsKey("quote") || !header.ContainsKey("source"))
                throw new SourceConversionException($"csv: input at {inputPath} is missing a required 'quote' or 'source' column");

            dataRows = rows.Skip(1).ToList();
        }
        else
        {
            header = null;
            if (csvOptions.HasHeader)
            {
                if (rows.Count < 2)
                    throw new SourceConversionException($"csv: input at {inputPath} has no header row or no data rows");
                dataRows = rows.Skip(1).ToList();
            }
            else
            {
                dataRows = rows;
            }
        }

        var quotes = new List<SourceQuote>();
        foreach (var row in dataRows)
        {
            string? Get(string canonicalName, int? mappedIndex) => mapping is not null
                ? (mappedIndex is { } idx && idx >= 1 && idx <= row.Count ? row[idx - 1] : null)
                : Field(row, header!, canonicalName);

            var genresRaw = Get("genres", mapping?.Genres);
            var genres    = string.IsNullOrWhiteSpace(genresRaw)
                ? defaults?.Genres?.ToList()
                : genresRaw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

            var quote = MappedSourceQuoteBuilder.Build(
                id:               Get("id", mapping?.Id),
                quote:            Get("quote", mapping?.Quote),
                originalLanguage: MappedSourceQuoteBuilder.Resolve(Get("originalLanguage", mapping?.OriginalLanguage), defaults?.OriginalLanguage),
                source:           Get("source", mapping?.Source),
                date:             MappedSourceQuoteBuilder.Resolve(Get("date", mapping?.Date), defaults?.Date),
                character:        MappedSourceQuoteBuilder.Resolve(Get("character", mapping?.Character), defaults?.Character),
                author:           MappedSourceQuoteBuilder.Resolve(Get("author", mapping?.Author), defaults?.Author),
                typeRaw:          Get("type", mapping?.Type) ?? defaults?.Type?.ToString(),
                genres:           genres);

            if (quote is not null) quotes.Add(quote);
        }

        if (quotes.Count == 0)
            throw new SourceConversionException($"csv: none of the {dataRows.Count} row(s) in {inputPath} had both a quote and a source");

        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(quotes, WriteOptions), cancellationToken);
    }

    private static string? Field(List<string> row, Dictionary<string, int> header, string column)
        => header.TryGetValue(column, out var idx) && idx < row.Count ? row[idx] : null;
}

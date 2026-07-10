using System.Text.Json;
using System.Text.RegularExpressions;
using Quotinator.Core.Import;
using Quotinator.Data.Import;

namespace Quotinator.Converters.RegexArray;

/// <summary>
/// Converts a JSON array of bare strings into Quotinator's canonical quote schema, by applying a
/// manifest-supplied regex <see cref="RegexArrayConverterOptions.Pattern"/> to each entry and mapping
/// its capture groups to canonical fields via <see cref="RegexArrayConverterOptions.GroupMapping"/>'s
/// 1-based index — the same indexing convention <c>CsvConverterOptions.ColumnMapping</c> uses. A raw
/// entry the pattern doesn't match is skipped, not an error, unless zero entries match at all. A row's
/// own <c>id</c> is used verbatim when present and non-empty; otherwise one is derived deterministically
/// via <see cref="QuoteIdentity.StableId"/>, exactly like the other bundled converters do for sources
/// with no id of their own.
/// </summary>
public sealed class RegexArrayConverter : IQuoteSourceConverter
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <inheritdoc/>
    public string Name => "regex-array";

    /// <inheritdoc/>
    public async Task ConvertAsync(string inputPath, string outputPath, JsonElement? options = null, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(inputPath, cancellationToken);

        List<string>? rawEntries;
        try
        {
            rawEntries = JsonSerializer.Deserialize<List<string>>(json);
        }
        catch (JsonException ex)
        {
            throw new SourceConversionException($"regex-array: input at {inputPath} is not a valid JSON string array", ex);
        }

        if (rawEntries is null || rawEntries.Count == 0)
            throw new SourceConversionException($"regex-array: input at {inputPath} contained no entries");

        var regexOptions = options?.Deserialize<RegexArrayConverterOptions>() ?? new RegexArrayConverterOptions();

        if (string.IsNullOrWhiteSpace(regexOptions.Pattern))
            throw new SourceConversionException($"regex-array: converterOptions for {inputPath} is missing a required 'pattern'");

        if (regexOptions.GroupMapping is null)
            throw new SourceConversionException($"regex-array: converterOptions for {inputPath} is missing a required 'groupMapping'");

        var pattern  = new Regex(regexOptions.Pattern, RegexOptions.Compiled);
        var mapping  = regexOptions.GroupMapping;
        var defaults = regexOptions.Defaults;

        var quotes = new List<SourceQuote>();
        foreach (var raw in rawEntries)
        {
            var match = pattern.Match(raw);
            if (!match.Success) continue;

            string? Get(int? groupIndex) => groupIndex is { } idx && idx >= 1 && idx < match.Groups.Count && match.Groups[idx].Success
                ? match.Groups[idx].Value
                : null;

            var genresRaw = Get(mapping.Genres);
            var genres    = string.IsNullOrWhiteSpace(genresRaw)
                ? defaults?.Genres?.ToList()
                : genresRaw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

            var quote = MappedSourceQuoteBuilder.Build(
                id:               Get(mapping.Id),
                quote:            Get(mapping.Quote),
                originalLanguage: MappedSourceQuoteBuilder.Resolve(Get(mapping.OriginalLanguage), defaults?.OriginalLanguage),
                source:           Get(mapping.Source),
                date:             MappedSourceQuoteBuilder.Resolve(Get(mapping.Date), defaults?.Date),
                character:        MappedSourceQuoteBuilder.Resolve(Get(mapping.Character), defaults?.Character),
                author:           MappedSourceQuoteBuilder.Resolve(Get(mapping.Author), defaults?.Author),
                typeRaw:          Get(mapping.Type) ?? defaults?.Type?.ToString(),
                genres:           genres);

            if (quote is not null) quotes.Add(quote);
        }

        if (quotes.Count == 0)
            throw new SourceConversionException($"regex-array: none of the {rawEntries.Count} entries in {inputPath} matched the configured pattern");

        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(quotes, WriteOptions), cancellationToken);
    }
}

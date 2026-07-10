using System.Text.Json;
using Quotinator.Core.Import;
using Quotinator.Data.Import;

namespace Quotinator.Converters.BasicJsonArray;

/// <summary>
/// Converts a flat JSON array of objects into Quotinator's canonical quote schema. With no
/// <see cref="BasicJsonArrayConverterOptions"/> supplied, each canonical field is read from the raw
/// JSON property of the same name (e.g. <c>quote</c>, <c>source</c>) — a source whose raw property
/// names already match Quotinator's canonical names needs no configuration at all. With
/// <see cref="BasicJsonArrayConverterOptions.PropertyMapping"/> supplied, a canonical field is instead
/// read from the named raw property. A row's own <c>id</c> is used verbatim when present and
/// non-empty; otherwise one is derived deterministically via <see cref="QuoteIdentity.StableId"/>,
/// exactly like the other bundled converters do for sources with no id of their own.
/// </summary>
public sealed class BasicJsonArrayConverter : IQuoteSourceConverter
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <inheritdoc/>
    public string Name => "basic-json-array";

    /// <inheritdoc/>
    public async Task ConvertAsync(string inputPath, string outputPath, JsonElement? options = null, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(inputPath, cancellationToken);

        List<Dictionary<string, JsonElement>>? rawEntries;
        try
        {
            rawEntries = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json)
                ?.Select(d => new Dictionary<string, JsonElement>(d, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }
        catch (JsonException ex)
        {
            throw new SourceConversionException($"basic-json-array: input at {inputPath} is not a valid JSON object array", ex);
        }

        if (rawEntries is null || rawEntries.Count == 0)
            throw new SourceConversionException($"basic-json-array: input at {inputPath} contained no entries");

        var jsonOptions = options?.Deserialize<BasicJsonArrayConverterOptions>() ?? new BasicJsonArrayConverterOptions();
        var mapping     = jsonOptions.PropertyMapping;
        var defaults    = jsonOptions.Defaults;

        var quotes = new List<SourceQuote>();
        foreach (var entry in rawEntries)
        {
            string? Get(string canonicalName, string? mappedProperty)
                => entry.TryGetValue(mappedProperty ?? canonicalName, out var element) ? ElementToString(element) : null;

            var genresKey = mapping?.Genres ?? "genres";
            var genres    = entry.TryGetValue(genresKey, out var genresElement) ? ParseGenres(genresElement) : null;
            genres ??= defaults?.Genres?.ToList();

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
            throw new SourceConversionException($"basic-json-array: none of the {rawEntries.Count} entries in {inputPath} had both a quote and a source");

        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(quotes, WriteOptions), cancellationToken);
    }

    /// <summary>Reads a raw value that may be a JSON string or a JSON number (upstream data is not always consistently typed), normalising it to a plain string.</summary>
    private static string? ElementToString(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        _                    => null,
    };

    /// <summary>A genres value may be a JSON array of strings (each element becomes one genre) or a single JSON string (becomes one genre) — no delimiter splitting needed, unlike CSV, since JSON expresses arrays natively.</summary>
    private static List<string>? ParseGenres(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Array => element.EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : null)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .ToList(),
        JsonValueKind.String when element.GetString() is { Length: > 0 } s => [s],
        _ => null,
    };
}

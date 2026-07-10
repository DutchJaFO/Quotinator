using System.Text.Json.Serialization;
using Quotinator.Core.Import;

namespace Quotinator.Converters.Csv;

/// <summary>
/// Configuration for <see cref="CsvQuoteConverter"/>, supplied via a manifest entry's or import
/// request's <c>converterOptions</c>. Every property is optional — an entirely empty (or absent)
/// instance preserves the converter's original zero-config behaviour: header-name auto-matching
/// against Quotinator's own canonical column names.
/// </summary>
public sealed class CsvConverterOptions
{
    /// <summary>
    /// Whether the first row is a header label row (skipped as data) rather than the first data row.
    /// Only meaningful when <see cref="ColumnMapping"/> is set — the zero-config path always requires
    /// and reads a header row for its column-name matching. Defaults to <c>true</c>.
    /// </summary>
    [JsonPropertyName("hasHeader")]
    public bool HasHeader { get; init; } = true;

    /// <summary>
    /// Explicit 1-based column-index mapping, overriding header-name auto-matching for every field it
    /// covers. When <c>null</c> (the default), columns are matched to canonical property names by
    /// header text instead.
    /// </summary>
    [JsonPropertyName("columnMapping")]
    public IndexedFieldMapping? ColumnMapping { get; init; }

    /// <summary>Literal default values for fields not sourced from any column.</summary>
    [JsonPropertyName("defaults")]
    public QuoteFieldDefaults? Defaults { get; init; }
}

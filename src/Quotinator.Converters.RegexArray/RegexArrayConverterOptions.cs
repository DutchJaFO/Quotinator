using System.Text.Json.Serialization;
using Quotinator.Core.Import;

namespace Quotinator.Converters.RegexArray;

/// <summary>
/// Configuration for <see cref="RegexArrayConverter"/>, supplied via a manifest entry's or import
/// request's <c>converterOptions</c>. Unlike <c>CsvConverterOptions</c>/
/// <c>BasicJsonArrayConverterOptions</c>, there is no zero-config path — a bare regex capture group
/// has no inherent name to match a canonical field against, so both <see cref="Pattern"/> and
/// <see cref="GroupMapping"/> are required.
/// </summary>
public sealed class RegexArrayConverterOptions
{
    /// <summary>
    /// The regex pattern applied to each raw string entry. Not <c>required</c> at the C# level —
    /// validated explicitly by <see cref="RegexArrayConverter"/> so a missing pattern throws
    /// <c>SourceConversionException</c> like every other unrecoverable input case, not a raw JSON
    /// deserialization exception.
    /// </summary>
    [JsonPropertyName("pattern")]
    public string? Pattern { get; init; }

    /// <summary>Which 1-based regex capture group each canonical quote field is read from.</summary>
    [JsonPropertyName("groupMapping")]
    public IndexedFieldMapping? GroupMapping { get; init; }

    /// <summary>Literal default values for fields not sourced from any capture group.</summary>
    [JsonPropertyName("defaults")]
    public QuoteFieldDefaults? Defaults { get; init; }
}

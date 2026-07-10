using System.Text.Json.Serialization;
using Quotinator.Core.Import;

namespace Quotinator.Converters.BasicJsonArray;

/// <summary>
/// Configuration for <see cref="BasicJsonArrayConverter"/>, supplied via a manifest entry's or import
/// request's <c>converterOptions</c>. Every property is optional — an entirely empty (or absent)
/// instance preserves the converter's zero-config behaviour: reading each canonical field from the
/// raw JSON property of the same name.
/// </summary>
public sealed class BasicJsonArrayConverterOptions
{
    /// <summary>
    /// Maps a canonical field to the raw JSON property name it should actually be read from, for raw
    /// data whose own property names don't already match Quotinator's canonical names (e.g. the
    /// source title is under a <c>"movie"</c> key rather than <c>"source"</c>). A canonical field left
    /// unmapped reads the raw property matching its own canonical name.
    /// </summary>
    [JsonPropertyName("propertyMapping")]
    public NamedFieldMapping? PropertyMapping { get; init; }

    /// <summary>Literal default values for fields not present in the raw entry at all.</summary>
    [JsonPropertyName("defaults")]
    public QuoteFieldDefaults? Defaults { get; init; }
}

using System.Text.Json.Serialization;
using Quotinator.Core.Models;

namespace Quotinator.Core.Import;

/// <summary>
/// An explicit Source declaration deserialized from a Quotinator source file's <c>sources</c> section
/// (#162). Decouples matching from content — a Source found by <see cref="Id"/> can have its
/// <see cref="Title"/>/<see cref="Type"/>/<see cref="Date"/> corrected, unlike a Source only ever
/// discovered implicitly through a quote's own title string, which is matched by natural key.
/// </summary>
public sealed class SourceEntry
{
    /// <summary>Unique identifier (UUID v4). Assigned at authoring time and never changes.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The title of the source in its original language.</summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>Media category. Same wire format (kebab-case, e.g. <c>movie</c>) as <see cref="SourceQuote.Type"/>.</summary>
    [JsonPropertyName("type")]
    [JsonConverter(typeof(QuoteTypeJsonConverter))]
    public QuoteType Type { get; init; } = QuoteType.Movie;

    /// <summary>Publication or release date. Imprecise ISO 8601 text (e.g. "1994", "1994-06"). Null when unknown.</summary>
    [JsonPropertyName("date")]
    public string? Date { get; init; }

    /// <summary>
    /// Name of the Series (#180) this Source belongs to, if any. Resolved to a Series id at import
    /// time — never a raw id, matching how a quote's own <c>source</c>/<c>author</c> fields are text.
    /// </summary>
    [JsonPropertyName("seriesName")]
    public string? SeriesName { get; init; }
}

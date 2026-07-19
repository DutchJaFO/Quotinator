using System.Text.Json.Serialization;
using Quotinator.Core.Models;
using Quotinator.Data.Import;

namespace Quotinator.Core.Import;

/// <summary>
/// A Source declaration deserialized from a Quotinator source file's <c>sources</c> section. Two
/// shapes, distinguished by whether <see cref="Id"/> is present:
/// <list type="bullet">
/// <item><b>Correction</b> (#162, <see cref="Id"/> set) — matched by that explicit id, decoupling
/// matching from content, so <see cref="Title"/>/<see cref="Type"/>/<see cref="Date"/> can all be
/// corrected.</item>
/// <item><b>Enrichment</b> (#180, <see cref="Id"/> omitted) — matched by natural key
/// (<see cref="Title"/> + <see cref="Type"/>), which by definition makes those two the lookup key
/// rather than correctable values. Only <see cref="SeriesName"/> is diffed on this path. Exists so a
/// curated overlay file never has to author an id this project generates for itself.</item>
/// </list>
/// </summary>
public sealed class SourceEntry
{
    /// <summary>
    /// Unique identifier (UUID v4). Assigned at authoring time and never changes. Omit it to match by
    /// natural key instead — see this class's own remarks for the two shapes.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>The title of the source in its original language.</summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>Media category. Same wire format (kebab-case, e.g. <c>movie</c>) as <see cref="SourceQuote.Type"/>.</summary>
    [JsonPropertyName("type")]
    [JsonConverter(typeof(QuoteTypeJsonConverter))]
    public QuoteType Type { get; init; } = QuoteType.Movie;

    /// <summary>
    /// Publication or release date. Imprecise ISO 8601 text (e.g. "1994", "1994-06"). Absent means
    /// leave the existing value alone; present with <c>null</c> means reset it (#190).
    /// </summary>
    [JsonPropertyName("date")]
    public Optional<string> Date { get; init; }

    /// <summary>
    /// Name of the Series (#180) this Source belongs to, if any. Resolved to a Series id at import
    /// time — never a raw id, matching how a quote's own <c>source</c>/<c>author</c> fields are text.
    /// Absent means leave the existing Series link alone; present with <c>null</c> means clear it (#190).
    /// </summary>
    [JsonPropertyName("seriesName")]
    public Optional<string> SeriesName { get; init; }
}

using Quotinator.Core.Enums;
using Quotinator.Data.Models;

namespace Quotinator.Core.Queries;

/// <summary>Read model for a single translation-resolved Quote, including its Source/Character/Author/Series/Universe context.</summary>
public sealed class QuoteRow
{
    /// <summary>Unique identifier.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The quote text, resolved to the requested language when a translation exists.</summary>
    public string QuoteText { get; init; } = string.Empty;

    /// <summary>ISO 639-1 code of the quote's original (untranslated) language.</summary>
    public string OriginalLanguage { get; init; } = "en";

    /// <summary>The Source title, resolved to the requested language when a translation exists.</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Imprecise ISO 8601 date of the Source, as stored.</summary>
    public string? Date { get; init; }

    /// <summary>The Source's type.</summary>
    public SafeValue<QuoteType?> SourceType { get; init; } = SafeValue<QuoteType?>.Empty;

    /// <summary>The Character name, resolved to the requested language when a translation exists.</summary>
    public string? Character { get; init; }

    /// <summary>The Person (author) name.</summary>
    public string? Author { get; init; }

    /// <summary>The language actually returned — the requested language if a translation was found, otherwise <see cref="OriginalLanguage"/>.</summary>
    public string EffectiveLanguage { get; init; } = string.Empty;

    /// <summary>Id of the Source's Series, when linked.</summary>
    public string? SeriesId { get; init; }

    /// <summary>Name of the Source's Series, when linked.</summary>
    public string? SeriesName { get; init; }

    /// <summary>Id of the Series' Universe, when linked.</summary>
    public string? UniverseId { get; init; }

    /// <summary>Name of the Series' Universe, when linked.</summary>
    public string? UniverseName { get; init; }
}

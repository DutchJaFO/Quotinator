using Quotinator.Core.Models;

namespace Quotinator.Core.Import;

/// <summary>
/// Shared row-assembly logic for the mapped-configuration converters (CSV, basic JSON-array, regex
/// array). Each converter resolves its own raw row into 9 field values (via <see cref="Resolve"/>,
/// reading from its own raw-lookup mechanism — column index, JSON property name, or regex group index
/// — plus its typed defaults class), then calls <see cref="Build"/> once to assemble the result.
/// </summary>
public static class MappedSourceQuoteBuilder
{
    /// <summary>Coalesces a raw row value with its configured default — empty/whitespace counts as absent.</summary>
    public static string? Resolve(string? rawValue, string? defaultValue) =>
        !string.IsNullOrWhiteSpace(rawValue) ? rawValue.Trim() : defaultValue;

    /// <summary>
    /// Assembles one row's already-resolved field values into a <see cref="SourceQuote"/>, or
    /// <c>null</c> if <paramref name="quote"/>/<paramref name="source"/> ended up empty — the same
    /// "skip this row" contract every converter already has. Derives <see cref="SourceQuote.Id"/> via
    /// <see cref="QuoteIdentity.StableId"/> when <paramref name="id"/> is absent, and applies the same
    /// <c>en</c>/<see cref="QuoteType.Movie"/> fallbacks every existing converter already uses.
    /// </summary>
    public static SourceQuote? Build(
        string? id, string? quote, string? originalLanguage, string? source, string? date,
        string? character, string? author, string? typeRaw, IReadOnlyList<string>? genres)
    {
        var trimmedQuote  = quote?.Trim();
        var trimmedSource = source?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedQuote) || string.IsNullOrWhiteSpace(trimmedSource))
            return null;

        return new SourceQuote
        {
            Id               = string.IsNullOrWhiteSpace(id) ? QuoteIdentity.StableId(trimmedQuote, trimmedSource) : id.Trim(),
            QuoteText        = trimmedQuote,
            OriginalLanguage = string.IsNullOrWhiteSpace(originalLanguage) ? "en" : originalLanguage.Trim(),
            Source           = trimmedSource,
            Date             = string.IsNullOrWhiteSpace(date) ? null : date.Trim(),
            Character        = string.IsNullOrWhiteSpace(character) ? null : character.Trim(),
            Author           = string.IsNullOrWhiteSpace(author) ? null : author.Trim(),
            Type             = QuoteTypeNormalisation.CanonicalType(typeRaw?.Trim(), QuoteType.Movie),
            Genres           = genres ?? [],
            Translations     = new Dictionary<string, SourceQuoteTranslation>(),
        };
    }
}

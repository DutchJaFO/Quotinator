using System.Text.Json;
using System.Text.RegularExpressions;
using Quotinator.Core.Models;

namespace Quotinator.Core.Helpers;

/// <summary>Shared validation helpers for API query parameters.</summary>
public static partial class InputValidation
{
    /// <summary>Maximum length allowed for free-text filter values (character, author, source).</summary>
    public const int MaxFilterLength = 200;

    /// <summary>The set of accepted quote source type values for the <c>type=</c> query parameter —
    /// derived from <see cref="QuoteType"/> (excluding the <see cref="QuoteType.Unknown"/> fallback
    /// sentinel) so the API vocabulary can never drift out of sync with the enum.</summary>
    public static readonly HashSet<string> ValidTypes =
        [.. Enum.GetValues<QuoteType>()
            .Where(t => t != QuoteType.Unknown)
            .Select(t => t.ToString().ToLowerInvariant())];

    /// <summary>The set of accepted field names for the <c>field=</c> parameter on the search endpoint.
    /// No backing enum exists for this — it names which column to search, not a stored value.</summary>
    public static readonly HashSet<string> ValidSearchFields =
        ["quote", "source", "character", "author"];

    /// <summary>The set of accepted genre tag values for the <c>genre=</c> query parameter — derived
    /// from <see cref="Genre"/> (excluding the <see cref="Genre.Unknown"/> fallback sentinel),
    /// kebab-cased to match the wire format (e.g. <c>SciFi</c> -&gt; <c>sci-fi</c>).</summary>
    public static readonly HashSet<string> ValidGenres =
        [.. Enum.GetValues<Genre>()
            .Where(g => g != Genre.Unknown)
            .Select(g => JsonNamingPolicy.KebabCaseLower.ConvertName(g.ToString()))];

    /// <summary>Maps API genre tags (e.g. <c>"sci-fi"</c>) to the database enum name (e.g. <c>"SciFi"</c>) —
    /// derived from <see cref="Genre"/>, not hand-duplicated. Shared between <c>SqliteQuoteService</c>
    /// (query normalisation) and <c>DatabaseInitializer</c> (seeding).</summary>
    public static readonly IReadOnlyDictionary<string, string> GenreApiToDb =
        Enum.GetValues<Genre>()
            .Where(g => g != Genre.Unknown)
            .ToDictionary(g => JsonNamingPolicy.KebabCaseLower.ConvertName(g.ToString()), g => g.ToString(),
                StringComparer.OrdinalIgnoreCase);

    // ISO 639-1: 2-letter code, optionally followed by a region subtag (e.g. en-GB).
    // Accepts up to 8 chars total to cover common BCP 47 subtags without being permissive.
    [GeneratedRegex(@"^[a-zA-Z]{2,3}(-[a-zA-Z]{2,4})?$")]
    private static partial Regex LangPattern();

    // Matches common SQL injection patterns: quote + OR/AND, UNION SELECT, statement
    // terminators before DML keywords, comment sequences, and EXEC calls.
    // Parameterised queries already prevent actual injection; this surfaces suspicious
    // input explicitly so callers receive a meaningful status rather than an empty result.
    [GeneratedRegex(@"(?i)('\s*(OR|AND)\s|;\s*(DROP|DELETE|INSERT|UPDATE|CREATE|EXEC)\b|\bUNION\s+SELECT\b|--|/\*|\bEXEC\s*\()")]
    private static partial Regex SuspiciousInputPattern();

    /// <summary>Returns <c>true</c> if <paramref name="lang"/> is a valid BCP 47 language tag in the expected form (e.g. "en", "en-GB", "nl").</summary>
    public static bool IsValidLang(string lang) =>
        lang.Length <= 8 && LangPattern().IsMatch(lang);

    /// <summary>Returns <c>true</c> if <paramref name="value"/> matches known SQL injection patterns.</summary>
    public static bool IsSuspiciousInput(string value) =>
        SuspiciousInputPattern().IsMatch(value);
}

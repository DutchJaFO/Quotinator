using System.Text.RegularExpressions;

namespace Quotinator.Core.Helpers;

/// <summary>Shared validation helpers for API query parameters.</summary>
public static partial class InputValidation
{
    /// <summary>Maximum length allowed for free-text filter values (character, author, source).</summary>
    public const int MaxFilterLength = 200;

    /// <summary>The set of accepted quote source type values for the <c>type=</c> query parameter.</summary>
    public static readonly HashSet<string> ValidTypes =
        ["movie", "tv", "anime", "book", "person"];

    /// <summary>The set of accepted field names for the <c>field=</c> parameter on the search endpoint.</summary>
    public static readonly HashSet<string> ValidSearchFields =
        ["quote", "source", "character", "author"];

    /// <summary>The set of accepted genre tag values for the <c>genre=</c> query parameter.</summary>
    public static readonly HashSet<string> ValidGenres =
        ["action", "adventure", "animation", "comedy", "drama", "fantasy", "fiction",
         "horror", "mystery", "non-fiction", "romance", "sci-fi", "thriller"];

    /// <summary>Maps API genre tags (e.g. <c>"sci-fi"</c>) to the database enum name (e.g. <c>"SciFi"</c>).
    /// Shared between <c>SqliteQuoteService</c> (query normalisation) and <c>DatabaseInitializer</c> (seeding).</summary>
    public static readonly IReadOnlyDictionary<string, string> GenreApiToDb =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["action"]      = "Action",
            ["adventure"]   = "Adventure",
            ["animation"]   = "Animation",
            ["comedy"]      = "Comedy",
            ["drama"]       = "Drama",
            ["fantasy"]     = "Fantasy",
            ["fiction"]     = "Fiction",
            ["horror"]      = "Horror",
            ["mystery"]     = "Mystery",
            ["non-fiction"] = "NonFiction",
            ["romance"]     = "Romance",
            ["sci-fi"]      = "SciFi",
            ["thriller"]    = "Thriller",
        };

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

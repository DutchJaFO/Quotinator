using System.Text.RegularExpressions;

namespace Quotinator.Core.Helpers;

/// <summary>Shared validation helpers for API query parameters.</summary>
public static partial class InputValidation
{
    /// <summary>The set of accepted quote source type values for the <c>type=</c> query parameter.</summary>
    public static readonly HashSet<string> ValidTypes =
        ["movie", "tv", "anime", "book", "person"];

    /// <summary>The set of accepted field names for the <c>field=</c> parameter on the search endpoint.</summary>
    public static readonly HashSet<string> ValidSearchFields =
        ["quote", "source", "character", "author"];

    // ISO 639-1: 2-letter code, optionally followed by a region subtag (e.g. en-GB).
    // Accepts up to 8 chars total to cover common BCP 47 subtags without being permissive.
    [GeneratedRegex(@"^[a-zA-Z]{2,3}(-[a-zA-Z]{2,4})?$")]
    private static partial Regex LangPattern();

    /// <summary>Returns <c>true</c> if <paramref name="lang"/> is a valid BCP 47 language tag in the expected form (e.g. "en", "en-GB", "nl").</summary>
    public static bool IsValidLang(string lang) =>
        lang.Length <= 8 && LangPattern().IsMatch(lang);
}

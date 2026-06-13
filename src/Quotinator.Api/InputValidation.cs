using System.Text.RegularExpressions;

namespace Quotinator.Api;

internal static partial class InputValidation
{
    internal static readonly HashSet<string> ValidTypes =
        ["movie", "tv", "anime", "book", "person"];

    // ISO 639-1: 2-letter code, optionally followed by a region subtag (e.g. en-GB).
    // Accepts up to 8 chars total to cover common BCP 47 subtags without being permissive.
    [GeneratedRegex(@"^[a-zA-Z]{2,3}(-[a-zA-Z]{2,4})?$")]
    private static partial Regex LangPattern();

    internal static bool IsValidLang(string lang) =>
        lang.Length <= 8 && LangPattern().IsMatch(lang);
}

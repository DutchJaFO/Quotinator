using Quotinator.Changelog.Models;

namespace Quotinator.Changelog.Formatting;

/// <summary>Renders a release's optional flavour quote as a markdown blockquote line.</summary>
public static class ChangelogQuoteFormatter
{
    /// <summary>
    /// Formats <paramref name="quote"/> as a markdown blockquote, e.g. <c>&gt; "Text" — Attribution</c>.
    /// Returns <see langword="null"/> when <paramref name="quote"/> is <see langword="null"/> or its text is empty.
    /// </summary>
    public static string? Format(ChangelogQuote? quote)
    {
        if (quote is null || string.IsNullOrWhiteSpace(quote.Text)) return null;

        return string.IsNullOrWhiteSpace(quote.Attribution)
            ? $"> \"{quote.Text}\""
            : $"> \"{quote.Text}\" — {quote.Attribution}";
    }
}

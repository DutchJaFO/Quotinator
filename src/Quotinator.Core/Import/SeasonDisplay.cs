namespace Quotinator.Core.Import;

/// <summary>
/// Renders a Season's human-readable name from its ordinal and its optional title and subtitle
/// (#375) — "Book One: Water" for Avatar: The Last Airbender's first season, and a number-only
/// season for a series whose seasons have no names of their own.
/// </summary>
public static class SeasonDisplay
{
    /// <summary>
    /// Formats a season for display. <paramref name="title"/> and <paramref name="subtitle"/> are each
    /// optional and independent — a season may carry both, only a title, or neither.
    /// </summary>
    /// <param name="number">The season's ordinal within its series.</param>
    /// <param name="title">The season's own name, or <see langword="null"/>.</param>
    /// <param name="subtitle">The season's subtitle, or <see langword="null"/>.</param>
    public static string Format(int number, string? title, string? subtitle)
    {
        string head = string.IsNullOrWhiteSpace(title)
            ? $"Season {number.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : title.Trim();

        return string.IsNullOrWhiteSpace(subtitle) ? head : $"{head}: {subtitle.Trim()}";
    }
}

namespace Quotinator.Changelog.Models;

/// <summary>Localised display names for the five changelog sections.</summary>
/// <param name="Highlights">Display name for the Highlights section.</param>
/// <param name="Added">Display name for the Added section.</param>
/// <param name="Changed">Display name for the Changed section.</param>
/// <param name="Fixed">Display name for the Fixed section.</param>
/// <param name="Removed">Display name for the Removed section.</param>
public sealed record ChangelogSectionHeaders(
    string Highlights,
    string Added,
    string Changed,
    string Fixed,
    string Removed);

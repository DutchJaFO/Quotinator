namespace Quotinator.Changelog.Models;

/// <summary>Localised display names for the five changelog sections.</summary>
public sealed class ChangelogSectionHeaders
{
    /// <summary>Display name for the Highlights section.</summary>
    public string Highlights { get; init; } = "";

    /// <summary>Display name for the Added section.</summary>
    public string Added { get; init; } = "";

    /// <summary>Display name for the Changed section.</summary>
    public string Changed { get; init; } = "";

    /// <summary>Display name for the Fixed section.</summary>
    public string Fixed { get; init; } = "";

    /// <summary>Display name for the Removed section.</summary>
    public string Removed { get; init; } = "";
}

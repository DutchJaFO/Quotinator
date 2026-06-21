namespace Quotinator.Changelog.Models;

/// <summary>Translations of all user-facing content for one release in a target language.</summary>
public sealed class ChangelogReleaseTranslation
{
    /// <summary>Translated highlight items.</summary>
    public List<ChangelogTranslationItem> Highlights { get; init; } = [];

    /// <summary>Translated added-section items.</summary>
    public List<ChangelogTranslationItem> Added { get; init; } = [];

    /// <summary>Translated changed-section items.</summary>
    public List<ChangelogTranslationItem> Changed { get; init; } = [];

    /// <summary>Translated fixed-section items.</summary>
    public List<ChangelogTranslationItem> Fixed { get; init; } = [];

    /// <summary>Translated removed-section items.</summary>
    public List<ChangelogTranslationItem> Removed { get; init; } = [];
}

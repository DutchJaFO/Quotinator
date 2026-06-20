namespace Quotinator.Changelog.Models;

/// <summary>Translations of all user-facing content for one release in a target language.</summary>
/// <param name="Highlights">Translated highlight items.</param>
/// <param name="Added">Translated added-section items.</param>
/// <param name="Changed">Translated changed-section items.</param>
/// <param name="Fixed">Translated fixed-section items.</param>
/// <param name="Removed">Translated removed-section items.</param>
public sealed record ChangelogReleaseTranslation(
    IReadOnlyList<ChangelogTranslationItem> Highlights,
    IReadOnlyList<ChangelogTranslationItem> Added,
    IReadOnlyList<ChangelogTranslationItem> Changed,
    IReadOnlyList<ChangelogTranslationItem> Fixed,
    IReadOnlyList<ChangelogTranslationItem> Removed);

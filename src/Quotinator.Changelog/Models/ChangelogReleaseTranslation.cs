namespace Quotinator.Changelog.Models;

/// <summary>Manually curated translations of the user-facing highlights for one release.</summary>
/// <param name="Highlights">Translated highlight strings, one per sentence.</param>
public sealed record ChangelogReleaseTranslation(IReadOnlyList<string> Highlights);

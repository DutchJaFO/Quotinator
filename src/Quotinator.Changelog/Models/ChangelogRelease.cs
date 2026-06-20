namespace Quotinator.Changelog.Models;

/// <summary>A single versioned release from <c>changelog.json</c>.</summary>
/// <param name="Version">Version string, e.g. <c>1.1.0</c>.</param>
/// <param name="Date">Release date in ISO 8601 format, e.g. <c>2026-06-15</c>.</param>
/// <param name="Highlights">User-friendly summary items. Empty when no highlights are present.</param>
/// <param name="Sections">Technical change sections (Added, Fixed, Changed, Removed), in document order. Does not include highlights.</param>
/// <param name="Issues">GitHub issue numbers addressed by this release.</param>
/// <param name="Cves">CVE IDs addressed by this release (e.g. <c>CVE-2025-6965</c>).</param>
/// <param name="Translations">Manually curated translations of highlights, keyed by ISO 639-1 language code.</param>
public sealed record ChangelogRelease(
    string Version,
    string Date,
    IReadOnlyList<string> Highlights,
    IReadOnlyList<ChangelogSection> Sections,
    IReadOnlyList<int> Issues,
    IReadOnlyList<string> Cves,
    IReadOnlyDictionary<string, ChangelogReleaseTranslation> Translations);

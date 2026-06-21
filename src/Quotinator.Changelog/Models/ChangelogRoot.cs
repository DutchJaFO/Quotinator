namespace Quotinator.Changelog.Models;

/// <summary>Top-level structure of <c>changelog.json</c>.</summary>
public sealed class ChangelogRoot
{
    /// <summary>ISO 639-1 language code of the source content. Defaults to <c>en</c> when absent.</summary>
    public string? SourceLanguage { get; init; }

    /// <summary>Localised display names for the five changelog sections, keyed by ISO 639-1 code.</summary>
    public Dictionary<string, ChangelogSectionHeaders>? SectionHeaders { get; init; }

    /// <summary>Pending changes not yet in a release. <see langword="null"/> when no <c>unreleased</c> block is present.</summary>
    public ChangelogUnreleased? Unreleased { get; init; }

    /// <summary>All released versions, newest first.</summary>
    public List<ChangelogRelease>? Releases { get; init; }
}

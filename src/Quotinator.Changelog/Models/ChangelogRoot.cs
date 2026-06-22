namespace Quotinator.Changelog.Models;

/// <summary>Top-level structure of a per-language changelog file (e.g. <c>changelog.en.json</c>).</summary>
public sealed class ChangelogRoot
{
    /// <summary>ISO 639-1 language code of the content in this file (e.g. <c>en</c>, <c>nl</c>, <c>de</c>).</summary>
    public string? Language { get; init; }

    /// <summary>ISO 639-1 code of the fallback language file. When equal to <see cref="Language"/> this file is authoritative with no further fallback.</summary>
    public string? SourceLanguage { get; init; }

    /// <summary><see langword="true"/> when the content of this file was machine-translated rather than manually curated.</summary>
    public bool MachineTranslated { get; init; }

    /// <summary>Localised display names for the five changelog sections in the language of this file.</summary>
    public ChangelogSectionHeaders? SectionHeaders { get; init; }

    /// <summary>Pending changes not yet in a release. <see langword="null"/> when no <c>unreleased</c> block is present.</summary>
    public ChangelogUnreleased? Unreleased { get; init; }

    /// <summary>All released versions, newest first.</summary>
    public List<ChangelogRelease>? Releases { get; init; }
}

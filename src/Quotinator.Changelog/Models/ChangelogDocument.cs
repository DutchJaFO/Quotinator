namespace Quotinator.Changelog.Models;

/// <summary>
/// A fully-resolved changelog for one language, produced by <see cref="Services.IChangelogService"/>.
/// Carries file-level metadata alongside the release content.
/// </summary>
public sealed class ChangelogDocument
{
    /// <summary>ISO 639-1 language code of this document's content.</summary>
    public string Language { get; init; } = "en";

    /// <summary><see langword="true"/> when the content was machine-translated rather than manually curated.</summary>
    public bool MachineTranslated { get; init; }

    /// <summary>Pending unreleased changes. <see langword="null"/> when no unreleased block is present.</summary>
    public ChangelogUnreleased? Unreleased { get; init; }

    /// <summary>All releases, newest first.</summary>
    public IReadOnlyList<ChangelogRelease> Releases { get; init; } = [];

    /// <summary>Section display names for this language. <see langword="null"/> when not declared in the source file.</summary>
    public ChangelogSectionHeaders? SectionHeaders { get; init; }
}

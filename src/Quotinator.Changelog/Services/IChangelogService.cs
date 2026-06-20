using Quotinator.Changelog.Models;

namespace Quotinator.Changelog.Services;

/// <summary>Provides release data deserialised from <c>changelog.json</c>.</summary>
public interface IChangelogService
{
    /// <summary>All releases, newest first.</summary>
    IReadOnlyList<ChangelogRelease> Releases { get; }

    /// <summary>ISO 639-1 language code of the top-level source content. Defaults to <c>"en"</c> when not declared in the JSON.</summary>
    string SourceLanguage { get; }

    /// <summary>
    /// Section display names per language, keyed by ISO 639-1 code.
    /// Falls back to <see cref="SourceLanguage"/> per section key when the requested language block is absent or incomplete.
    /// Empty when no <c>sectionHeaders</c> block is present in the JSON.
    /// </summary>
    IReadOnlyDictionary<string, ChangelogSectionHeaders> SectionHeaders { get; }
}

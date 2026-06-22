using Quotinator.Changelog.Models;

namespace Quotinator.Changelog.Services;

/// <summary>Provides changelog content resolved to the requested language.</summary>
public interface IChangelogService
{
    /// <summary>
    /// Returns the changelog document for <paramref name="culture"/>, falling back to <c>en</c>
    /// when no file exists for the requested language.
    /// Returns <see langword="null"/> when no file is found at all — signals language not found / not supported.
    /// </summary>
    ChangelogDocument? GetForCulture(string? culture);

    /// <summary>
    /// ISO 639-1 codes of all language files successfully loaded at startup.
    /// Use the count to verify that all expected files were read — a count lower than the number
    /// of <c>changelog.*.json</c> files on disk indicates a parse failure or missing <c>language</c> property.
    /// </summary>
    IReadOnlyList<string> AvailableLanguages { get; }
}

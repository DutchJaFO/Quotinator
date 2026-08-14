using Quotinator.Changelog.Models;

namespace Quotinator.Data.Repositories;

/// <summary>
/// Reads changelog content resolved to the requested language, preferring the separate changelog
/// database and falling back to <see cref="Changelog.Services.IChangelogService"/>'s JSON-backed
/// content when the database is unavailable (#309, ADR 018). Both <c>About.razor</c> and #81's future
/// producer read through this interface, never <see cref="Changelog.Services.IChangelogService"/>
/// directly.
/// </summary>
public interface IChangelogReader
{
    /// <summary>
    /// Returns the changelog document for <paramref name="culture"/>, falling back to <c>en</c> when
    /// no content exists for the requested language. Returns <see langword="null"/> when no content is
    /// found at all — signals language not found / not supported.
    /// </summary>
    Task<ChangelogDocument?> GetDocumentAsync(string? culture);
}

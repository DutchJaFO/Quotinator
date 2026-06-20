using Quotinator.Changelog.Models;

namespace Quotinator.Changelog.Services;

/// <summary>Provides release data deserialised from <c>changelog.json</c>.</summary>
public interface IChangelogService
{
    /// <summary>All releases, newest first.</summary>
    IReadOnlyList<ChangelogRelease> Releases { get; }
}

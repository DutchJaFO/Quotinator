using Quotinator.Changelog.Enums;

namespace Quotinator.Changelog.Models;

/// <summary>
/// Pending changes not yet assigned a version or date.
/// Base class for <see cref="ChangelogRelease"/>, which adds <see cref="ChangelogRelease.Version"/> and <see cref="ChangelogRelease.Date"/>.
/// </summary>
public class ChangelogUnreleased
{
    /// <summary>GitHub issue numbers this work addresses.</summary>
    public List<int> Issues { get; init; } = [];

    /// <summary>CVE IDs this work addresses.</summary>
    public List<string> Cves { get; init; } = [];

    /// <summary>User-facing plain-English summary items.</summary>
    public List<string> Highlights { get; init; } = [];

    /// <summary>Technical items added.</summary>
    public List<string> Added { get; init; } = [];

    /// <summary>Technical items changed.</summary>
    public List<string> Changed { get; init; } = [];

    /// <summary>Technical items fixed.</summary>
    public List<string> Fixed { get; init; } = [];

    /// <summary>Technical items removed.</summary>
    public List<string> Removed { get; init; } = [];

    /// <summary>Audience-specific highlight overrides, keyed by audience name (e.g. <c>ha-addon</c>).</summary>
    public Dictionary<string, List<string>> AudienceHighlights { get; init; } = [];

    /// <summary>
    /// Highlights for a reserved audience — e.g. <see cref="ChangelogReservedAudience.Notification"/>.
    /// Returns an empty list when the key is absent; never falls back to <see cref="Highlights"/>,
    /// unlike <c>scripts/changelog.csx</c>'s own markdown-generation fallback behaviour.
    /// </summary>
    public List<string> GetHighlightsFor(ChangelogReservedAudience audience) =>
        AudienceHighlights.GetValueOrDefault(audience switch
        {
            ChangelogReservedAudience.Notification => "notification",
            _ => throw new ArgumentOutOfRangeException(nameof(audience))
        }, []);
}

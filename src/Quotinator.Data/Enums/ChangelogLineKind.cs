namespace Quotinator.Data.Enums;

/// <summary>
/// Discriminates a <see cref="Entities.ChangelogLineEntity"/> row — every list-shaped field on a
/// changelog release (<c>highlights</c>, <c>added</c>, <c>changed</c>, <c>fixed</c>, <c>removed</c>,
/// <c>issues</c>, <c>cves</c>, <c>audienceHighlights</c>) is stored as rows in one child table rather
/// than one column each, per #309's master/detail schema (<see cref="Repositories.AggregateRepository{TParent,TChild}"/>
/// is generic over exactly one child type).
/// </summary>
public enum ChangelogLineKind
{
    /// <summary>User-facing plain-English summary item.</summary>
    Highlight,

    /// <summary>Technical item added.</summary>
    Added,

    /// <summary>Technical item changed.</summary>
    Changed,

    /// <summary>Technical item fixed.</summary>
    Fixed,

    /// <summary>Technical item removed.</summary>
    Removed,

    /// <summary>GitHub issue number this work addresses.</summary>
    Issue,

    /// <summary>CVE ID this work addresses.</summary>
    Cve,

    /// <summary>
    /// An entry under a specific <c>audienceHighlights</c> key (see
    /// <see cref="Entities.ChangelogLineEntity.AudienceKey"/>) — e.g. the reserved
    /// <c>Quotinator.Changelog.Enums.ChangelogReservedAudience.Notification</c> key #307 established.
    /// </summary>
    AudienceHighlight
}

using Dapper.Contrib.Extensions;
using Quotinator.Data.Enums;
using Quotinator.Data.Models;

namespace Quotinator.Data.Entities;

/// <summary>
/// One list-item line belonging to a <see cref="ChangelogEntity"/> — the child half of #309's
/// master/detail schema. Discriminated by <see cref="Kind"/> so every list-shaped changelog field
/// (highlights, added, changed, fixed, removed, issues, cves, audienceHighlights) is covered by one
/// child table instead of one column each.
/// </summary>
[Table("ChangelogLine")]
public sealed class ChangelogLineEntity : RecordBase
{
    /// <summary>The owning <see cref="ChangelogEntity"/>.</summary>
    public Guid ChangelogId { get; init; }

    /// <summary>Which list-shaped field this line belongs to.</summary>
    public SafeValue<ChangelogLineKind?> Kind { get; init; } = SafeValue<ChangelogLineKind?>.Empty;

    /// <summary>
    /// Populated only when <see cref="Kind"/> is <see cref="ChangelogLineKind.AudienceHighlight"/> —
    /// the <c>audienceHighlights</c> key this line belongs to (e.g. #307's reserved
    /// <c>"notification"</c> key). <see langword="null"/> for every other kind.
    /// </summary>
    public string? AudienceKey { get; init; }

    /// <summary>The line's text. Stores <see cref="ChangelogLineKind.Issue"/> numbers as their string form.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>Preserves the original list order — child rows otherwise have none.</summary>
    public int SortOrder { get; init; }
}

using Dapper.Contrib.Extensions;
using Quotinator.Data.Models;

namespace Quotinator.Data.Entities;

/// <summary>
/// One changelog release (or, when <see cref="Version"/> is <see langword="null"/>, a language's
/// <c>unreleased</c> entry), mirrored from <c>data/changelog/changelog.*.json</c> into the separate
/// changelog database (#309, ADR 018). Parent half of the master/detail pair with
/// <see cref="ChangelogLineEntity"/> — every list-shaped field (highlights, added, etc.) is stored as
/// child rows rather than columns on this entity.
/// </summary>
[Table("Changelog")]
public sealed class ChangelogEntity : RecordBase
{
    /// <summary>ISO 639-1 language code (matches one of the <c>changelog.*.json</c> files).</summary>
    public string Language { get; init; } = string.Empty;

    /// <summary>Semver release version, or <see langword="null"/> for that language's <c>unreleased</c> entry.</summary>
    public string? Version { get; init; }

    /// <summary>Release date (ISO 8601), or <see langword="null"/> for <c>unreleased</c>.</summary>
    public string? Date { get; init; }

    /// <summary>Optional release-note flavour quote text.</summary>
    public string? QuoteText { get; init; }

    /// <summary>Optional attribution for <see cref="QuoteText"/>.</summary>
    public string? QuoteAttribution { get; init; }
}

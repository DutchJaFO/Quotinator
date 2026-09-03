using Dapper.Contrib.Extensions;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Models;

namespace Quotinator.Core.Entities;

/// <summary>
/// An ordered grouping of Sources within a Series (ADR 011) — a television season, a magazine's
/// volume, a podcast's season. Deliberately not television-specific: nothing here keys off
/// <c>Source.Type</c>, and no behaviour is conditioned on the medium.
/// </summary>
[Table("Quotinator_Season")]
public sealed class SeasonEntity : RecordBase
{
    /// <summary>
    /// The season's ordinal within its series, and its natural key alongside <see cref="SeriesId"/>.
    /// Unlike Series and Universe, whose <c>Name</c> is globally unique, an ordinal only means
    /// something within its parent — "Season 1" recurs for every series.
    /// </summary>
    public int Number { get; init; }

    /// <summary>The season's own name, where it has one — "Book One" for Avatar: The Last Airbender's first season. Null when the season is identified by its number alone.</summary>
    public string? Title { get; init; }

    /// <summary>The season's subtitle, where it has one — "Water" for that same season, rendering "Book One: Water". Null when absent.</summary>
    public string? Subtitle { get; init; }

    /// <summary>The series this season belongs to, if any.</summary>
    public Guid? SeriesId { get; init; }

    /// <summary>The import batch that introduced this record. Null for records predating provenance tracking.</summary>
    public Guid? ImportBatchId { get; init; }

    /// <summary>
    /// Whether the record's fields are known to be fully populated and reviewed (#55/#165).
    /// <see cref="Quotinator.Data.Enums.CompletenessStatus.Complete"/> is human-set only.
    /// </summary>
    public SafeValue<CompletenessStatus?> CompletenessStatus { get; init; } = SafeValue<CompletenessStatus?>.Empty;

    /// <summary>Field names confirmed to have no findable value.</summary>
    public IReadOnlyList<string> NoValueKnown { get; init; } = [];
}

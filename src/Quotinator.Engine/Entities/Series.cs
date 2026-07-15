using Dapper.Contrib.Extensions;
using Quotinator.Data.Entities;
using Quotinator.Data.Models;

namespace Quotinator.Engine.Entities;

/// <summary>A direct continuity of Sources within a Universe (e.g. "The Lord of the Rings" trilogy).</summary>
[Table("Series")]
public sealed class SeriesEntity : RecordBase
{
    /// <summary>The series' name. Unique.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The universe this series belongs to, if any. A standalone series has none.</summary>
    public Guid? UniverseId { get; init; }

    /// <summary>The import batch that introduced this record. Null for records predating provenance tracking.</summary>
    public Guid? ImportBatchId { get; init; }

    /// <summary>
    /// Whether the record's fields are known to be fully populated and reviewed (#55/#165).
    /// <see cref="Quotinator.Data.Entities.CompletenessStatus.Complete"/> is human-set only.
    /// </summary>
    public SafeValue<CompletenessStatus?> CompletenessStatus { get; init; } = SafeValue<CompletenessStatus?>.Empty;

    /// <summary>Field names confirmed to have no findable value.</summary>
    public IReadOnlyList<string> NoValueKnown { get; init; } = [];
}

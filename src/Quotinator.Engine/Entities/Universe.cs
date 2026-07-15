using Dapper.Contrib.Extensions;
using Quotinator.Data.Entities;
using Quotinator.Data.Models;

namespace Quotinator.Engine.Entities;

/// <summary>A fictional world or franchise spanning one or more Series (e.g. "Middle Earth").</summary>
[Table("Universe")]
public sealed class UniverseEntity : RecordBase
{
    /// <summary>The universe's name. Unique.</summary>
    public string Name { get; init; } = string.Empty;

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

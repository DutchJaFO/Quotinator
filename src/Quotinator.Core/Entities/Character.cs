using Dapper.Contrib.Extensions;
using Quotinator.Data.Entities;
using Quotinator.Data.Models;

namespace Quotinator.Core.Entities;

/// <summary>A fictional character who delivers a quote. May appear in multiple Sources — see <see cref="CharacterSourceEntity"/> (#179).</summary>
[Table("Characters")]
public sealed class Character : RecordBase
{
    /// <summary>The character's name in the source's original language.</summary>
    public string Name     { get; init; } = string.Empty;

    /// <summary>The import batch that introduced this record. Null for records predating provenance tracking.</summary>
    public Guid? ImportBatchId { get; init; }

    /// <summary>
    /// Whether the record's fields are known to be fully populated and reviewed (#55/#165).
    /// <see cref="Quotinator.Data.Entities.CompletenessStatus.Complete"/> is human-set only.
    /// </summary>
    public SafeValue<CompletenessStatus?> CompletenessStatus { get; init; } = SafeValue<CompletenessStatus?>.Empty;

    /// <summary>Field names confirmed to have no findable value. Kept for consistency with the other three entity types even though <see cref="Name"/> currently has no such candidate.</summary>
    public IReadOnlyList<string> NoValueKnown { get; init; } = [];
}

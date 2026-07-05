using Dapper.Contrib.Extensions;
using Quotinator.Data.Models;

namespace Quotinator.Engine.Entities;

/// <summary>A fictional character who delivers a quote, scoped to their source.</summary>
[Table("Characters")]
public sealed class Character : RecordBase
{
    /// <summary>The source this character belongs to. Scoping prevents same-name characters from different franchises from colliding.</summary>
    public Guid   SourceId { get; init; }

    /// <summary>The character's name in the source's original language.</summary>
    public string Name     { get; init; } = string.Empty;

    /// <summary>The import batch that introduced this record. Null for records predating provenance tracking.</summary>
    public Guid? ImportBatchId { get; init; }

    /// <summary>Human-set flag meaning the record has been reviewed and is satisfactory. Never set automatically by enrichment or import.</summary>
    public bool IsComplete { get; init; }

    /// <summary>Field names confirmed to have no findable value. Kept for consistency with the other three entity types even though <see cref="Name"/> currently has no such candidate.</summary>
    public IReadOnlyList<string> NoValueKnown { get; init; } = [];
}

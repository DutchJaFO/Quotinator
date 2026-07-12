using Dapper.Contrib.Extensions;
using Quotinator.Data.Entities;
using Quotinator.Data.Models;

namespace Quotinator.Engine.Entities;

/// <summary>A reusable scene-setting or action description (e.g. "[EXT. AIRPORT - DAY]") that can appear in a conversation.</summary>
[Table("StageDirections")]
public sealed class StageDirectionEntity : RecordBase
{
    /// <summary>The stage direction text in its original language.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Optional image (e.g. a production still) illustrating the scene.</summary>
    public string? ImageUrl { get; init; }

    /// <summary>The import batch that introduced this record. Null for records predating provenance tracking.</summary>
    public Guid? ImportBatchId { get; init; }

    /// <summary>
    /// Whether the record's fields are known to be fully populated and reviewed (#165).
    /// <see cref="Quotinator.Data.Entities.CompletenessStatus.Complete"/> is human-set only.
    /// </summary>
    public SafeValue<CompletenessStatus?> CompletenessStatus { get; init; } = SafeValue<CompletenessStatus?>.Empty;

    /// <summary>Field names confirmed to have no findable value.</summary>
    public IReadOnlyList<string> NoValueKnown { get; init; } = [];
}

namespace Quotinator.Core.Models;

/// <summary>One row of the unified staging workflow (#154), mirroring a <c>System_ImportActions</c> row.</summary>
public sealed class ImportActionSummaryResponse
{
    /// <summary>The action's own Id.</summary>
    public required Guid Id { get; init; }

    /// <summary>The batch this action was staged under.</summary>
    public required string BatchId { get; init; }

    /// <summary><c>"Add"</c> or <c>"Modify"</c>.</summary>
    public required string ActionType { get; init; }

    /// <summary>Entity type the action applies to — <c>"Quote"</c>, <c>"Source"</c>, <c>"Character"</c>, or <c>"Person"</c>.</summary>
    public required string EntityType { get; init; }

    /// <summary>Identifier of the affected entity.</summary>
    public required string EntityId { get; init; }

    /// <summary>The batch that originally created the <i>existing</i> side of a Modify action. Null for an Add.</summary>
    public string? ExistingBatchId { get; init; }

    /// <summary><c>"Pending"</c>, <c>"Decided"</c>, <c>"Applied"</c>, or <c>"Discarded"</c>.</summary>
    public required string Status { get; init; }

    /// <summary>The duplicate-resolution policy applied while staging this action, when applicable.</summary>
    public string? AppliedPolicy { get; init; }

    /// <summary>UTC timestamp when the action was staged.</summary>
    public required DateTime DetectedAt { get; init; }

    /// <summary>UTC timestamp when the owning batch was applied. Null until then.</summary>
    public DateTime? AppliedAt { get; init; }

    /// <summary>UTC timestamp when the owning batch was discarded. Null unless discarded.</summary>
    public DateTime? DiscardedAt { get; init; }

    /// <summary>Field values from the row already in the database. Null for an Add — there is no existing side.</summary>
    public IReadOnlyDictionary<string, object?>? ExistingFields { get; init; }

    /// <summary>Field values from the incoming row.</summary>
    public required IReadOnlyDictionary<string, object?> IncomingFields { get; init; }

    /// <summary>The fully resolved field values that would be written. Null until <see cref="Status"/> is no longer <c>"Pending"</c>.</summary>
    public IReadOnlyDictionary<string, object?>? MergedFields { get; init; }

    /// <summary>
    /// Ids of other staged actions in the same batch this action depends on — e.g. a Quote action's
    /// own Source/Character/Person Add actions. Always empty for a Source/Character/Person action.
    /// </summary>
    public IReadOnlyList<Guid> RelatedActionIds { get; init; } = [];

    /// <summary>
    /// Field names genuinely needing an explicit decision. Only populated for a <c>Pending</c> Quote
    /// action — empty for every other entity type and status.
    /// </summary>
    public IReadOnlyList<string> AmbiguousFields { get; init; } = [];
}

namespace Quotinator.Core.Models;

/// <summary>One row of the manual conflict-review workflow (#149), mirroring a <c>System_ImportConflicts</c> row.</summary>
public sealed class ConflictSummaryResponse
{
    /// <summary>The conflict's own Id.</summary>
    public required Guid Id { get; init; }

    /// <summary>Entity type the conflict occurred on — always <c>"Quote"</c> today.</summary>
    public required string EntityType { get; init; }

    /// <summary>Id of the affected entity.</summary>
    public string? EntityId { get; init; }

    /// <summary><c>"pending"</c>, <c>"decided"</c>, or <c>"resolved"</c>.</summary>
    public required string Status { get; init; }

    /// <summary>The batch during which the conflict was detected (the <i>incoming</i> side).</summary>
    public required string BatchId { get; init; }

    /// <summary>Human-readable label for <see cref="BatchId"/> (the import batch's own name), when resolvable.</summary>
    public string? BatchLabel { get; init; }

    /// <summary>The batch that originally created the <i>existing</i> side, when known.</summary>
    public string? ExistingBatchId { get; init; }

    /// <summary>Human-readable label for <see cref="ExistingBatchId"/>, when resolvable.</summary>
    public string? ExistingBatchLabel { get; init; }

    /// <summary><c>true</c> when both sides of the conflict came from the same imported file/batch (<see cref="ExistingBatchId"/> == <see cref="BatchId"/>).</summary>
    public required bool SameFile { get; init; }

    /// <summary>The duplicate-resolution policy applied when the conflict was detected (wire value).</summary>
    public string? AppliedPolicy { get; init; }

    /// <summary>UTC timestamp when the conflict was detected.</summary>
    public required DateTime DetectedAt { get; init; }

    /// <summary>UTC timestamp when the conflict was resolved. Null while still pending or decided.</summary>
    public DateTime? ResolvedAt { get; init; }

    /// <summary>Field values from the row already in the database.</summary>
    public required QuoteConflictFieldsDto ExistingFields { get; init; }

    /// <summary>Field values from the incoming row.</summary>
    public required QuoteConflictFieldsDto IncomingFields { get; init; }

    /// <summary>
    /// Field names where both sides are non-empty and differ — these are the fields that genuinely
    /// need an explicit decision; everything else auto-resolves. Empty once <see cref="Status"/> is
    /// no longer <c>"pending"</c>.
    /// </summary>
    public IReadOnlyList<string> AmbiguousFields { get; init; } = [];
}

using Dapper.Contrib.Extensions;
using Quotinator.Data.Import;
using Quotinator.Data.Models;

namespace Quotinator.Data.Entities;

/// <summary>
/// Records a single planned action (an add or a modify) computed while staging an import or seed
/// run — logged for every row a batch would touch, not only ones that are genuinely ambiguous.
/// This is what makes a staged batch fully inspectable and undoable before (and after) it commits.
/// </summary>
/// <remarks>
/// RecordBase-shaped from creation (#154) — unlike <see cref="SystemImportConflict"/>, this table
/// never existed before ADR 002, so no create-then-retrofit migration pair is needed.
/// <c>ExistingValue</c>/<c>IncomingValue</c>/<c>MergedFields</c> are opaque JSON blobs — this
/// project never deserializes them; the consuming project (e.g. Quotinator.Engine) produces and
/// later interprets that content, since this project has no dependency on any specific domain
/// schema. Not a candidate for <see cref="Quotinator.Data.Helpers.JsonHandler{T}"/>, for the same
/// reason as <see cref="SystemImportConflict"/>'s identically-named fields: the concrete shape is
/// owned by a consumer project (e.g. <c>Quotinator.Core.Models</c>/<c>Quotinator.Engine.Models</c>),
/// and typing this property that way would require <c>Quotinator.Data</c> to reference it, which
/// ADR 004 forbids. <see cref="EntityType"/> is likewise free-text, entirely caller-defined — Data
/// never branches on its value beyond storing/filtering by it.
/// </remarks>
[Table("System_ImportActions")]
public sealed class SystemImportAction : RecordBase
{
    /// <summary>Loose reference to the batch this action was staged under. No FK — this project doesn't know the consumer's batch table name.</summary>
    public string BatchId { get; init; } = string.Empty;

    /// <summary>The kind of action this row represents. <see cref="SafeValue{T}.Raw"/> preserves an unrecognised stored value for diagnosis.</summary>
    public SafeValue<ImportActionKind?> ActionType { get; init; } = SafeValue<ImportActionKind?>.Empty;

    /// <summary>Free-text entity type the action applies to (e.g. <c>"Quote"</c>).</summary>
    public string EntityType { get; init; } = string.Empty;

    /// <summary>Identifier of the affected entity.</summary>
    public string EntityId { get; init; } = string.Empty;

    /// <summary>Loose reference to the batch that originally created the <i>existing</i> side of a Modify action. Null for an Add.</summary>
    public string? ExistingBatchId { get; init; }

    /// <summary>Opaque JSON snapshot of the existing record's field values at staging time. Null for an Add.</summary>
    public string? ExistingValue { get; init; }

    /// <summary>Opaque JSON snapshot of the incoming record's field values. Always set.</summary>
    public string IncomingValue { get; init; } = string.Empty;

    /// <summary>The conflict-resolution policy applied while staging this action, when applicable.</summary>
    public SafeValue<DuplicateResolutionPolicy?> AppliedPolicy { get; init; } = SafeValue<DuplicateResolutionPolicy?>.Empty;

    /// <summary>Opaque JSON blob recording, per field, which side won — populated at staging time for every Modify (ambiguous or not), never for an Add.</summary>
    public string? MergedFields { get; init; }

    /// <summary>The action's current state. <see cref="SafeValue{T}.Raw"/> preserves an unrecognised stored value for diagnosis.</summary>
    public SafeValue<ImportActionStatus?> Status { get; init; } = SafeValue<ImportActionStatus?>.Empty;

    /// <summary>UTC timestamp when the action was staged.</summary>
    public DateTime DetectedAt { get; init; }

    /// <summary>UTC timestamp when the owning batch was applied. Null until then.</summary>
    public DateTime? AppliedAt { get; init; }

    /// <summary>UTC timestamp when the owning batch was discarded. Null unless discarded.</summary>
    public DateTime? DiscardedAt { get; init; }
}

/// <summary>
/// The states a <see cref="SystemImportAction"/> row can be in — a closed set defined and
/// maintained entirely by this project's own coordinator logic (<see cref="Import.IImportActionCoordinator"/>),
/// not by any consuming project's schema. Per ADR 008, backed by a matching SQL CHECK constraint.
/// </summary>
public enum ImportActionStatus
{
    /// <summary>Genuinely ambiguous — needs an explicit decision before the owning batch can be applied.</summary>
    Pending,

    /// <summary>Auto-resolved at staging time (every Add and unambiguous Modify), or a decision has been recorded for a Pending action. Ready to apply.</summary>
    Decided,

    /// <summary>The owning batch was applied — this action's write landed on the consumer's own tables.</summary>
    Applied,

    /// <summary>The owning batch was discarded — this action was never written anywhere.</summary>
    Discarded
}

/// <summary>
/// The kinds of action a <see cref="SystemImportAction"/> row can represent — a closed set defined
/// and maintained entirely by this project's own coordinator logic, not by any consuming project's
/// schema (a consumer decides, per row, which of these two kinds applies — it does not invent new
/// kinds of its own). Per ADR 008, backed by a matching SQL CHECK constraint.
/// </summary>
public enum ImportActionKind
{
    /// <summary>A brand-new record with no existing counterpart.</summary>
    Add,

    /// <summary>An existing record whose fields would change.</summary>
    Modify
}

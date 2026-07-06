using Dapper.Contrib.Extensions;
using Quotinator.Data.Import;
using Quotinator.Data.Models;

namespace Quotinator.Data.Entities;

/// <summary>
/// Records a single detected duplicate/conflict during an import or seed run — logged for every
/// conflict regardless of which <see cref="DuplicateResolutionPolicy"/> was applied, not only ones
/// left pending for manual review.
/// </summary>
/// <remarks>
/// Extends <see cref="RecordBase"/> per ADR 002 ("RecordBase applies to all tables without
/// exception") — <see cref="RecordBase.DateModified"/>/<see cref="RecordBase.DateDeleted"/>/
/// <see cref="RecordBase.IsDeleted"/> are never meaningfully used here (a resolved conflict's own
/// <see cref="Status"/>/<see cref="ResolvedAt"/> already capture its one real state transition, and
/// rows are never soft-deleted), and <see cref="RecordBase.DateCreated"/> duplicates
/// <see cref="DetectedAt"/> — this redundancy is the ADR's own accepted trade-off. Unreleased at the
/// time of this change, so the column-type change (auto-increment <c>long</c> -&gt; Guid) was made by
/// editing <c>ImportConflictMigrations.CreateImportConflictsTable</c> directly rather than a new
/// migration (contrast <see cref="SystemAuditEntry"/>, already shipped in v1.7.2, which needed one).
/// <c>ExistingValue</c>/<c>IncomingValue</c>/<c>MergedFields</c> are opaque JSON blobs — this
/// project never deserializes them; the consuming project (e.g. Quotinator.Engine) produces and later
/// diffs that content, since this project has no dependency on any specific domain schema.
/// <para>
/// <b>When #149 (manual conflict-review workflow) starts:</b> if it needs to read these fields back as
/// structured data rather than a raw string, use <see cref="Quotinator.Data.Helpers.JsonHandler{T}"/> via
/// <see cref="Quotinator.Data.Helpers.DatabaseConfiguration.RegisterJsonHandler{T}"/> (e.g. registering it for
/// <c>IReadOnlyDictionary&lt;string, string&gt;</c> to type <see cref="MergedFields"/>, or
/// <c>IReadOnlyDictionary&lt;string, object?&gt;</c> for <see cref="ExistingValue"/>/<see cref="IncomingValue"/>)
/// from <c>QuotinatorDapperConfiguration.RegisterDomainHandlers()</c> — not by changing these properties'
/// types here, which would break the domain-agnostic design this class deliberately keeps.
/// </para>
/// </remarks>
[Table("System_ImportConflicts")]
public sealed class SystemImportConflict : RecordBase
{
    /// <summary>Loose reference to the import batch during which this conflict was detected (the <i>incoming</i> side). No FK — this project doesn't know the consumer's batch table name.</summary>
    public string BatchId { get; init; } = string.Empty;

    /// <summary>Loose reference to the import batch that originally created the <i>existing</i> side of this conflict. Null when unknown. Equal to <see cref="BatchId"/> when both sides of the conflict came from the same imported file/batch.</summary>
    public string? ExistingBatchId { get; init; }

    /// <summary>Free-text entity type the conflict occurred on (e.g. <c>"Quote"</c>).</summary>
    public string EntityType { get; init; } = string.Empty;

    /// <summary>Identifier of the affected entity, when applicable.</summary>
    public string? EntityId { get; init; }

    /// <summary>Opaque JSON snapshot of the existing (first-seen) record's field values at the time the conflict was detected.</summary>
    public string? ExistingValue { get; init; }

    /// <summary>Opaque JSON snapshot of the incoming record's field values at the time the conflict was detected.</summary>
    public string? IncomingValue { get; init; }

    /// <summary>The conflict-resolution policy that was applied to resolve this specific conflict.</summary>
    public SafeValue<DuplicateResolutionPolicy?> AppliedPolicy { get; init; } = SafeValue<DuplicateResolutionPolicy?>.Empty;

    /// <summary>One of the <see cref="ImportConflictStatus"/> constants.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Opaque JSON blob recording, per field, which side won — populated only when <see cref="AppliedPolicy"/>'s parsed value is <see cref="DuplicateResolutionPolicy.MergeOurs"/> or <see cref="DuplicateResolutionPolicy.MergeTheirs"/>.</summary>
    public string? MergedFields { get; init; }

    /// <summary>UTC timestamp when the conflict was detected.</summary>
    public DateTime DetectedAt { get; init; }

    /// <summary>UTC timestamp when the conflict was resolved. Null while <see cref="Status"/> is <see cref="ImportConflictStatus.Pending"/>.</summary>
    public DateTime? ResolvedAt { get; init; }
}

/// <summary>String constants for the two states a <see cref="SystemImportConflict"/> row can be in.</summary>
public static class ImportConflictStatus
{
    /// <summary>The conflict was auto-resolved at detection time (every policy except <see cref="DuplicateResolutionPolicy.Review"/>).</summary>
    public const string Resolved = "resolved";

    /// <summary>The conflict is awaiting manual review (<see cref="DuplicateResolutionPolicy.Review"/> today).</summary>
    public const string Pending = "pending";

    /// <summary>A per-field decision has been recorded (#149) but the owning batch hasn't been applied yet — nothing has been written to any domain table.</summary>
    public const string Decided = "decided";
}

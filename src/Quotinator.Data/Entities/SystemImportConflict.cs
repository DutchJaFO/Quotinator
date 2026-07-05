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
/// Does not extend <see cref="RecordBase"/> — like <see cref="SystemAuditEntry"/>, these rows
/// are append-only (a resolved conflict is never deleted, only its <see cref="Status"/>/<see cref="ResolvedAt"/>
/// updated). <c>ExistingValue</c>/<c>IncomingValue</c>/<c>MergedFields</c> are opaque JSON blobs — this
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
public sealed class SystemImportConflict
{
    /// <summary>Auto-increment surrogate key assigned by SQLite.</summary>
    [Key]
    public long Id { get; init; }

    /// <summary>Loose reference to the import batch this conflict was detected during. No FK — this project doesn't know the consumer's batch table name.</summary>
    public string BatchId { get; init; } = string.Empty;

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
}

using Dapper.Contrib.Extensions;
using Quotinator.Data.Models;

namespace Quotinator.Data.Entities;

/// <summary>Records a single created/modified/deleted operation on a domain entity, with the initiating mechanism and (for modifications) the changed field values.</summary>
/// <remarks>
/// Extends <see cref="RecordBase"/> per ADR 002 ("RecordBase applies to all tables without
/// exception") — <see cref="RecordBase.DateModified"/>/<see cref="RecordBase.DateDeleted"/>/
/// <see cref="RecordBase.IsDeleted"/> are never meaningfully used here (a change-log row is never
/// itself modified or soft-deleted), and <see cref="RecordBase.DateCreated"/> duplicates
/// <see cref="OccurredAt"/> — this redundancy is the ADR's own accepted trade-off, same as
/// <see cref="SystemAuditEntry"/> and <see cref="SystemImportAction"/>.
/// <c>EntityType</c>/<c>EntityId</c> are plain strings, not enums — this project has no dependency on
/// any specific domain schema, so it cannot know which entity types a consumer defines, now or in the
/// future (mirrors <see cref="SystemImportAction.EntityType"/> for the identical reason).
/// <c>InitiatedByType</c>/<c>Action</c> ARE enums (<see cref="InitiatorType"/>/<see cref="ChangeAction"/>)
/// despite living in this domain-agnostic project — both vocabularies describe generic mechanisms/
/// operation kinds ("Seed", "Created") that don't require knowing anything about a consumer's specific
/// entities, unlike <c>EntityType</c>'s values.
/// </remarks>
[Table("System_ChangeLog")]
public sealed class SystemChangeLog : RecordBase
{
    /// <summary>Free-text entity type the change occurred on (e.g. <c>"quote"</c>). Not an enum — see remarks.</summary>
    public string EntityType { get; init; } = string.Empty;

    /// <summary>Identifier of the affected entity.</summary>
    public string EntityId { get; init; } = string.Empty;

    /// <summary>The mechanism that initiated this change. <see cref="SafeValue{T}.Raw"/> preserves an unrecognised stored value for diagnosis.</summary>
    public SafeValue<InitiatorType?> InitiatedByType { get; init; } = SafeValue<InitiatorType?>.Empty;

    /// <summary>Specific identifying detail for the initiator — an import batch UUID, an HTTP route, an enrichment provider name, or <c>null</c>.</summary>
    public string? InitiatedById { get; init; }

    /// <summary>The kind of database operation this row records.</summary>
    public SafeValue<ChangeAction?> Action { get; init; } = SafeValue<ChangeAction?>.Empty;

    /// <summary>Field name for a genuinely single-field change. <c>null</c> for whole-record snapshots.</summary>
    public string? Field { get; init; }

    /// <summary>Previous value(s) — a single field's value, or a JSON snapshot of the whole record, depending on <see cref="Field"/>.</summary>
    public string? OldValue { get; init; }

    /// <summary>New value(s) — a single field's value, or a JSON snapshot of the whole record, depending on <see cref="Field"/>.</summary>
    public string? NewValue { get; init; }

    /// <summary>UTC timestamp when the change occurred.</summary>
    public DateTime OccurredAt { get; init; }
}

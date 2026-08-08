using System.Text.Json.Serialization;
using Quotinator.Data.Enums;

namespace Quotinator.Core.Models;

/// <summary>Response shape for an <c>Audit_Change</c> row within <see cref="AuditExportResponse"/> — every <c>RecordBase</c> column unwrapped to its plain value instead of the internal <c>SafeValue&lt;T&gt;</c> wrapper, and the two enum-backed columns typed as their actual C# enum rather than a plain string (#272).</summary>
public sealed class AuditChangeResponse
{
    /// <summary>Canonical (lowercase) id.</summary>
    public required string Id { get; init; }

    /// <summary>Free-text entity type the change occurred on (e.g. <c>"quote"</c>).</summary>
    public required string EntityType { get; init; }

    /// <summary>Identifier of the affected entity.</summary>
    public required string EntityId { get; init; }

    /// <summary>The mechanism that initiated this change.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public InitiatorType? InitiatedByType { get; init; }

    /// <summary>Specific identifying detail for the initiator — an import batch UUID, an HTTP route, an enrichment provider name, or <see langword="null"/>.</summary>
    public string? InitiatedById { get; init; }

    /// <summary>The kind of database operation this row records.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ChangeAction? Action { get; init; }

    /// <summary>Field name for a genuinely single-field change. <see langword="null"/> for whole-record snapshots.</summary>
    public string? Field { get; init; }

    /// <summary>Previous value(s) — a single field's value, or a JSON snapshot of the whole record, depending on <see cref="Field"/>.</summary>
    public string? OldValue { get; init; }

    /// <summary>New value(s) — a single field's value, or a JSON snapshot of the whole record, depending on <see cref="Field"/>.</summary>
    public string? NewValue { get; init; }

    /// <summary>UTC timestamp when the change occurred.</summary>
    public DateTime OccurredAt { get; init; }

    /// <summary>UTC timestamp when the record was first written.</summary>
    public DateTime? DateCreated { get; init; }

    /// <summary>UTC timestamp of the most recent update. <see langword="null"/> unless the row was modified outside this project's own normal write path.</summary>
    public DateTime? DateModified { get; init; }

    /// <summary>UTC timestamp when the record was soft-deleted. <see langword="null"/> unless the row was deleted outside this project's own normal write path.</summary>
    public DateTime? DateDeleted { get; init; }

    /// <summary><see langword="true"/> when the record has been soft-deleted.</summary>
    public bool IsDeleted { get; init; }
}

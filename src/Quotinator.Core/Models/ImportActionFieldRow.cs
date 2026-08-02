using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Import;

namespace Quotinator.Core.Models;

/// <summary>
/// One field-level row of the bulk export/decide flat format (#163) — <c>GET /import/actions/export</c>
/// produces these, <c>POST /import/actions/bulk-decide</c> consumes them back. <see cref="Field"/> uses
/// the same camelCase vocabulary already exposed via <c>GET /import/actions</c>' own
/// <c>ExistingFields</c>/<c>IncomingFields</c>/<c>AmbiguousFields</c> (e.g. <c>"quoteText"</c>,
/// <c>"title"</c>, <c>"name"</c>) rather than <see cref="ConflictDecisionRequest"/>'s PascalCase property
/// names — <see cref="EntityType"/> and <see cref="Field"/> together identify a decidable field
/// unambiguously, since the same field name can mean different things on different entity types (e.g.
/// <c>"name"</c> on a Person row versus a Character row).
/// </summary>
public sealed class ImportActionFieldRow
{
    /// <summary>The <c>System_ImportActions</c> row this field belongs to.</summary>
    public required Guid ActionId { get; init; }

    /// <summary>The target record's own id (<c>SystemImportAction.EntityId</c>).</summary>
    public required string EntityId { get; init; }

    /// <summary>One of <see cref="Quotinator.Core.Helpers.ImportActionEntityTypes.All"/>.</summary>
    public required string EntityType { get; init; }

    /// <summary>
    /// The field name, scoped to <see cref="EntityType"/> — see
    /// <see cref="Quotinator.Core.Database.ImportActionFieldRowMapper.DecidableFieldsByEntityType"/> for
    /// the full per-entity list.
    /// </summary>
    public required string Field { get; init; }

    /// <summary>
    /// The existing side's current value, as plain text. A list-valued field (Quote's <c>genres</c>)
    /// is <c>;</c>-delimited — see
    /// <see cref="Quotinator.Core.Database.ImportActionFieldRowMapper.EncodeGenres"/>.
    /// </summary>
    public string? ExistingValue { get; init; }

    /// <summary>The incoming side's value, as plain text — same encoding as <see cref="ExistingValue"/>.</summary>
    public string? IncomingValue { get; init; }

    /// <summary>Which side wins, or <see cref="FieldResolutionChoice.Custom"/> for <see cref="CustomValue"/>. <c>null</c> supplies no decision for this field.</summary>
    public FieldResolutionChoice? Decision { get; init; }

    /// <summary>The caller-supplied value when <see cref="Decision"/> is <see cref="FieldResolutionChoice.Custom"/>. Ignored otherwise — same encoding as <see cref="ExistingValue"/>.</summary>
    public string? CustomValue { get; init; }

    /// <summary>
    /// One value per <c>ActionId</c> group, repeated on every row of that group (#163 developer
    /// decision) — resolves a <c>Blocked</c> hold the same way <see cref="ConflictDecisionRequest.MarkCompletenessAs"/> already does.
    /// </summary>
    public CompletenessStatus? MarkCompletenessAs { get; init; }
}

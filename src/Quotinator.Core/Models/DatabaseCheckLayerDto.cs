namespace Quotinator.Core.Models;

/// <summary>
/// One layer of a database check, as <c>GET /api/v1/admin/database/integrity</c> reports it (#490, ADR 028). A
/// member of <see cref="DatabaseIntegrityDto"/>, so it carries <c>Dto</c> rather than <c>Response</c> (ADR 016).
/// </summary>
public sealed class DatabaseCheckLayerDto
{
    /// <summary>The layer, lower case: <c>storage</c>, <c>format</c>, <c>journal</c>, <c>pages</c>, <c>indexes</c>, <c>references</c> or <c>schema</c>.</summary>
    public required string Layer { get; init; }

    /// <summary>What it found, lower case: <c>passed</c>, <c>failed</c>, <c>inconclusive</c>, <c>notrun</c> or <c>skipped</c>.</summary>
    public required string Status { get; init; }

    /// <summary>The engine's own diagnostic, or why the layer was inconclusive or had nothing to check; omitted when there is none.</summary>
    public string? Finding { get; init; }

    /// <summary>How long the layer took, in milliseconds; omitted when it did not run. An indicator, never a threshold.</summary>
    public double? ElapsedMilliseconds { get; init; }
}

namespace Quotinator.Core.Models;

/// <summary>
/// One database's integrity check result, as <c>GET /api/v1/admin/database/integrity</c> reports it
/// (#305). A member of <see cref="DatabaseIntegrityResponse"/>, so it carries <c>Dto</c> rather than
/// <c>Response</c> (ADR 016).
/// </summary>
public sealed class DatabaseIntegrityDto
{
    /// <summary>The name the database is reported under.</summary>
    public required string Database { get; init; }

    /// <summary>Whether nothing is missing or different. Extra objects leave a database intact.</summary>
    public required bool IsIntact { get; init; }

    /// <summary>Every difference found, missing, different and extra alike, sorted.</summary>
    public required IReadOnlyList<SchemaDifferenceDto> Differences { get; init; }
}

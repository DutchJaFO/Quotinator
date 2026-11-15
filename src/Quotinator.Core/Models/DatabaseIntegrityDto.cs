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

    /// <summary>
    /// Whether nothing is missing or different (#305). Extra objects leave a database intact. <see langword="false"/>
    /// with no differences when the file could not be read, so its schema could not be compared.
    /// </summary>
    public required bool IsIntact { get; init; }

    /// <summary>Every difference found, missing, different and extra alike, sorted.</summary>
    public required IReadOnlyList<SchemaDifferenceDto> Differences { get; init; }

    /// <summary>Whether every layer of the database check passed (#490, ADR 028), always checked at <c>Full</c>.</summary>
    public required bool IsSound { get; init; }

    /// <summary>Every layer of the database check, in the order it runs them, each with what it found.</summary>
    public required IReadOnlyList<DatabaseCheckLayerDto> Layers { get; init; }
}

namespace Quotinator.Core.Models;

/// <summary>
/// One way a database's schema differs from the one its migration path defines, as
/// <c>GET /api/v1/admin/database/integrity</c> reports it (#305). A member of
/// <see cref="DatabaseIntegrityDto"/>, so it carries <c>Dto</c> rather than <c>Response</c> (ADR 016).
/// </summary>
public sealed class SchemaDifferenceDto
{
    /// <summary><c>missing</c>, <c>different</c> or <c>extra</c>.</summary>
    public required string Kind { get; init; }

    /// <summary><c>table</c>, <c>column</c>, <c>foreignkey</c>, <c>index</c>, <c>view</c> or <c>trigger</c>.</summary>
    public required string ObjectKind { get; init; }

    /// <summary>The object's name, qualified by its table where it belongs to one.</summary>
    public required string ObjectName { get; init; }

    /// <summary>The compared property that differs, or <see langword="null"/> when the whole object is missing or extra.</summary>
    public string? Aspect { get; init; }

    /// <summary>The value the migration path defines, or <see langword="null"/> when there is none.</summary>
    public string? Expected { get; init; }

    /// <summary>The value the database holds, or <see langword="null"/> when there is none.</summary>
    public string? Actual { get; init; }
}

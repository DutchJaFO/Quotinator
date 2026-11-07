namespace Quotinator.Core.Models;

/// <summary>
/// Response envelope for <c>GET /api/v1/admin/database/integrity</c> (#305): the integrity check's result
/// for every database the application has, run when the request was made.
/// </summary>
public sealed class DatabaseIntegrityResponse
{
    /// <summary>One entry per database.</summary>
    public required IReadOnlyList<DatabaseIntegrityDto> Databases { get; init; }
}

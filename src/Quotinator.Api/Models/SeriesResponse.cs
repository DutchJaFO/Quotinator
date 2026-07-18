using Quotinator.Data.Entities;

namespace Quotinator.Api.Models;

/// <summary>The API response shape for a single Series — a direct continuity of Sources within a
/// Universe (#179).</summary>
public sealed class SeriesResponse
{
    /// <summary>Unique identifier (UUID v4).</summary>
    public required string Id { get; init; }

    /// <summary>The series' name.</summary>
    public required string Name { get; init; }

    /// <summary>The universe this series belongs to, if any (#179), as a minimal read-only reference —
    /// the universe's <c>Id</c>/<c>Name</c> only, resolved via <c>ISeriesUniverseReferenceReader</c>.
    /// <c>null</c> for a standalone series, and <c>null</c> if the linked universe has been
    /// soft-deleted (per CLAUDE.md's "Soft-deleted rows are invisible by default" convention).</summary>
    public MasterDataReference? Universe { get; init; }

    /// <summary>Whether the record's fields are known to be fully populated and reviewed.</summary>
    public required CompletenessStatus CompletenessStatus { get; init; }
}

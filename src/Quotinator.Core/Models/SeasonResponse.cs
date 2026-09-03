using Quotinator.Data.Enums;

namespace Quotinator.Core.Models;

/// <summary>The API response shape for a single Season — an ordered grouping of Sources within a
/// Series (#375, ADR 011).</summary>
public sealed class SeasonResponse
{
    /// <summary>Unique identifier (UUID v4).</summary>
    public required string Id { get; init; }

    /// <summary>The season's ordinal within its series.</summary>
    public required int Number { get; init; }

    /// <summary>The season's own name, where it has one — "Book One". <c>null</c> when the season is identified by its ordinal alone.</summary>
    public string? Title { get; init; }

    /// <summary>The season's subtitle, where it has one — "Water". <c>null</c> when absent.</summary>
    public string? Subtitle { get; init; }

    /// <summary>The three above combined for display — "Book One: Water", or "Season 3" for a season with no name of its own.</summary>
    public required string DisplayName { get; init; }

    /// <summary>The series this season belongs to, if any, as a minimal read-only reference — the
    /// series' <c>Id</c>/<c>Name</c> only. <c>null</c> when the season has no series, and <c>null</c>
    /// when the linked series has been soft-deleted (per CLAUDE.md's "Soft-deleted rows are invisible
    /// by default" convention).</summary>
    public MasterDataReference? Series { get; init; }

    /// <summary>Whether the record's fields are known to be fully populated and reviewed.</summary>
    public required CompletenessStatus CompletenessStatus { get; init; }
}

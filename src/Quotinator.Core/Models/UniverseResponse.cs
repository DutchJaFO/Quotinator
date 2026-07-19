namespace Quotinator.Core.Models;

/// <summary>The API response shape for a single Universe.</summary>
public sealed class UniverseResponse
{
    /// <summary>Unique identifier (UUID v4).</summary>
    public required string Id { get; init; }

    /// <summary>The universe's name. Unique.</summary>
    public required string Name { get; init; }

    /// <summary>Whether the record's fields are known to be fully populated and reviewed.</summary>
    public required Quotinator.Data.Entities.CompletenessStatus CompletenessStatus { get; init; }
}

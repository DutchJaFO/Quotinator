namespace Quotinator.Core.Models;

/// <summary>The API response shape for a single Person.</summary>
public sealed class PersonResponse
{
    /// <summary>Unique identifier (UUID v4).</summary>
    public required string Id { get; init; }

    /// <summary>The person's full name.</summary>
    public required string Name { get; init; }

    /// <summary>Imprecise ISO 8601 birth date (e.g. "1955" or "1955-02-24"). Null when unknown.</summary>
    public string? DateOfBirth { get; init; }

    /// <summary>Imprecise ISO 8601 death date. Null when still living or unknown.</summary>
    public string? DateOfDeath { get; init; }

    /// <summary>Whether the record's fields are known to be fully populated and reviewed. Null when not yet assessed.</summary>
    public Quotinator.Data.Entities.CompletenessStatus? CompletenessStatus { get; init; }
}

namespace Quotinator.Core.Models;

/// <summary>The API response shape for a single StageDirection.</summary>
public sealed class StageDirectionResponse
{
    /// <summary>Unique identifier (UUID v4).</summary>
    public required string Id { get; init; }

    /// <summary>The stage direction text in its original language.</summary>
    public required string Text { get; init; }

    /// <summary>Optional image (e.g. a production still) illustrating the scene. <c>null</c> when unset.</summary>
    public string? ImageUrl { get; init; }

    /// <summary>Whether the record's fields are known to be fully populated and reviewed.</summary>
    public required Quotinator.Data.Entities.CompletenessStatus CompletenessStatus { get; init; }
}

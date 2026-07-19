namespace Quotinator.Api.Models;

/// <summary>The API response shape for a single SoundCue.</summary>
public sealed class SoundCueResponse
{
    /// <summary>Unique identifier (UUID v4).</summary>
    public required string Id { get; init; }

    /// <summary>The sound cue text in its original language.</summary>
    public required string Text { get; init; }

    /// <summary>Optional audio file for the cue. <c>null</c> when unset.</summary>
    public string? SoundFileUrl { get; init; }

    /// <summary>Optional image illustrating the cue. <c>null</c> when unset.</summary>
    public string? ImageUrl { get; init; }

    /// <summary>Whether the record's fields are known to be fully populated and reviewed.</summary>
    public required Quotinator.Data.Entities.CompletenessStatus CompletenessStatus { get; init; }
}

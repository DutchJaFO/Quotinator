using Dapper.Contrib.Extensions;
using Quotinator.Data.Models;

namespace Quotinator.Engine.Entities;

/// <summary>A reusable audio element (e.g. "[awkward silence]") that can appear in a conversation.</summary>
[Table("SoundCues")]
public sealed class SoundCueEntity : RecordBase
{
    /// <summary>The sound cue text in its original language.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Optional audio file for the cue.</summary>
    public string? SoundFileUrl { get; init; }

    /// <summary>Optional image illustrating the cue.</summary>
    public string? ImageUrl { get; init; }

    /// <summary>The import batch that introduced this record. Null for records predating provenance tracking.</summary>
    public Guid? ImportBatchId { get; init; }
}

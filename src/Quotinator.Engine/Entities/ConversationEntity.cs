using Dapper.Contrib.Extensions;
using Quotinator.Data.Models;

namespace Quotinator.Engine.Entities;

/// <summary>An ordered grouping of quotes, stage directions, and sound cues (see <see cref="ConversationLineEntity"/>).</summary>
[Table("Conversations")]
public sealed class ConversationEntity : RecordBase
{
    /// <summary>Optional human-readable label for the conversation.</summary>
    public string? Description { get; init; }

    /// <summary>The import batch that introduced this record. Null for records predating provenance tracking.</summary>
    public Guid? ImportBatchId { get; init; }
}

using Dapper.Contrib.Extensions;
using Quotinator.Core.Models;
using Quotinator.Data.Models;

namespace Quotinator.Engine.Entities;

/// <summary>
/// One position in a <see cref="ConversationEntity"/>'s ordered line list. Exactly one of
/// <see cref="QuoteId"/>, <see cref="StageDirectionId"/>, <see cref="SoundCueId"/> is populated,
/// matching <see cref="LineType"/> — enforced in SQL by a CHECK constraint (see
/// <c>QuotinatorMigrations.Migration008_Conversations</c>), not just in application code.
/// </summary>
[Table("ConversationLines")]
public sealed class ConversationLineEntity : RecordBase
{
    /// <summary>The conversation this line belongs to.</summary>
    public Guid ConversationId { get; init; }

    /// <summary>1-based position of this line within the conversation. Unique per <see cref="ConversationId"/>.</summary>
    public int Order { get; init; }

    /// <summary>Which of <see cref="QuoteId"/>/<see cref="StageDirectionId"/>/<see cref="SoundCueId"/> is populated.</summary>
    public SafeValue<ConversationLineType?> LineType { get; init; } = SafeValue<ConversationLineType?>.Empty;

    /// <summary>Populated when <see cref="LineType"/> is <see cref="ConversationLineType.Quote"/>.</summary>
    public Guid? QuoteId { get; init; }

    /// <summary>Populated when <see cref="LineType"/> is <see cref="ConversationLineType.StageDirection"/>.</summary>
    public Guid? StageDirectionId { get; init; }

    /// <summary>Populated when <see cref="LineType"/> is <see cref="ConversationLineType.SoundCue"/>.</summary>
    public Guid? SoundCueId { get; init; }
}

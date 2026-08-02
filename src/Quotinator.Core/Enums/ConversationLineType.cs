namespace Quotinator.Core.Enums;

/// <summary>
/// Discriminates what one line of a conversation points to — shared by
/// <c>Quotinator.Core.Import.SourceConversationLineDto</c> (the JSON import DTO) and
/// <c>Quotinator.Core.Entities.ConversationLineEntity</c> (the database entity), the same way
/// <see cref="QuoteType"/> is shared between <c>SourceQuoteDto</c> and <c>Source</c>.
/// </summary>
public enum ConversationLineType
{
    /// <summary>The line is a quote.</summary>
    Quote,

    /// <summary>The line is a stage direction.</summary>
    StageDirection,

    /// <summary>The line is a sound cue.</summary>
    SoundCue
}

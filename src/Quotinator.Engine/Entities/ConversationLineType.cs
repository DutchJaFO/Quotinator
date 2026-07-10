namespace Quotinator.Engine.Entities;

/// <summary>Discriminates what a <see cref="ConversationLineEntity"/> row points to.</summary>
public enum ConversationLineType
{
    /// <summary><see cref="ConversationLineEntity.QuoteId"/> is populated; the other two FKs are <c>null</c>.</summary>
    Quote,

    /// <summary><see cref="ConversationLineEntity.StageDirectionId"/> is populated; the other two FKs are <c>null</c>.</summary>
    StageDirection,

    /// <summary><see cref="ConversationLineEntity.SoundCueId"/> is populated; the other two FKs are <c>null</c>.</summary>
    SoundCue
}

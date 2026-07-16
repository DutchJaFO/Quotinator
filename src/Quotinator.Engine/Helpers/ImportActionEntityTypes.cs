namespace Quotinator.Engine.Helpers;

/// <summary>
/// The canonical <see cref="Quotinator.Data.Entities.SystemImportAction.EntityType"/> values this
/// project's own code writes and compares against.
/// </summary>
/// <remarks>
/// <see cref="Quotinator.Data.Entities.SystemImportAction.EntityType"/> is deliberately free-text in
/// <c>Quotinator.Data</c> — per ADR 004, Data cannot reference Engine's entity types, so nothing in
/// Data can anchor these values to a shared enum. This class is Engine's own single source of truth
/// instead, replacing what were independent string literals repeated across
/// <c>ImportActionPlanner</c>, <c>QuotinatorDatabaseInitializer</c>, <c>SqliteImportActionService</c>,
/// and <c>SqliteQuoteImportService</c>.
/// </remarks>
public static class ImportActionEntityTypes
{
    /// <summary>A <c>Quotes</c> row.</summary>
    public const string Quote = "Quote";

    /// <summary>A <c>Sources</c> row.</summary>
    public const string Source = "Source";

    /// <summary>A <c>Characters</c> row.</summary>
    public const string Character = "Character";

    /// <summary>A <c>People</c> row.</summary>
    public const string Person = "Person";

    /// <summary>
    /// A <c>Conversations</c> row (#68). Add-only, id-keyed like <see cref="Quote"/> rather than
    /// natural-key-keyed like <see cref="Source"/>/<see cref="Character"/>/<see cref="Person"/> — a
    /// conversation's id is explicit in its source file, not <c>EntityIdentity</c>-derived. Its
    /// <c>ConversationLines</c> travel in the same action's payload, not as their own actions.
    /// </summary>
    public const string Conversation = "Conversation";

    /// <summary>A <c>StageDirections</c> row (#68). Add-only, id-keyed — see <see cref="Conversation"/>'s remark.</summary>
    public const string StageDirection = "StageDirection";

    /// <summary>A <c>SoundCues</c> row (#68). Add-only, id-keyed — see <see cref="Conversation"/>'s remark.</summary>
    public const string SoundCue = "SoundCue";

    /// <summary>
    /// A <c>Series</c> row (#180). Add-only, natural-key-keyed by <c>Name</c> like
    /// <see cref="Source"/>/<see cref="Person"/> — but with no Modify/decidability surface, since a
    /// Series has only a Name (plus its Universe link).
    /// </summary>
    public const string Series = "Series";

    /// <summary>A <c>Universe</c> row (#180). Add-only, natural-key-keyed by <c>Name</c> — see <see cref="Series"/>'s remark.</summary>
    public const string Universe = "Universe";

    /// <summary>All nine values.</summary>
    public static readonly string[] All = [Quote, Source, Character, Person, Conversation, StageDirection, SoundCue, Series, Universe];
}

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

    /// <summary>All four values.</summary>
    public static readonly string[] All = [Quote, Source, Character, Person];
}

namespace Quotinator.Core.Data;

/// <summary>Initialises the database schema and seed data at application startup.</summary>
public interface IDatabaseInitializer
{
    /// <summary>Schema version applied at startup. Available after <see cref="InitialiseAsync"/> completes.</summary>
    int SchemaVersion { get; }

    /// <summary>Total non-deleted quote rows. Updated by <see cref="InitialiseAsync"/>, <see cref="ReseedAsync"/>, and <see cref="ResetAsync"/>.</summary>
    int QuoteCount { get; }

    /// <summary>Total non-deleted source rows. Updated by <see cref="InitialiseAsync"/>, <see cref="ReseedAsync"/>, and <see cref="ResetAsync"/>.</summary>
    int SourceCount { get; }

    /// <summary>Total non-deleted character rows. Updated by <see cref="InitialiseAsync"/>, <see cref="ReseedAsync"/>, and <see cref="ResetAsync"/>.</summary>
    int CharacterCount { get; }

    /// <summary>Total non-deleted people rows. Updated by <see cref="InitialiseAsync"/>, <see cref="ReseedAsync"/>, and <see cref="ResetAsync"/>.</summary>
    int PeopleCount { get; }

    /// <summary>Ensures WAL mode is active, applies any pending schema migrations, and seeds the database from <c>quotes.json</c> if empty.</summary>
    Task InitialiseAsync();

    /// <summary>Clears all data tables and reimports from <c>quotes.json</c>. Schema migration history is preserved. Updates the row-count properties when done.</summary>
    Task ReseedAsync();

    /// <summary>Clears all data tables and schema migration history, reapplies all migrations, then reimports from <c>quotes.json</c>. Updates the row-count properties when done.</summary>
    Task ResetAsync();
}

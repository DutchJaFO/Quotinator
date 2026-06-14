namespace Quotinator.Core.Data;

/// <summary>Initialises the database schema and seed data at application startup.</summary>
public interface IDatabaseInitializer
{
    /// <summary>Schema version applied at startup. Available after <see cref="InitialiseAsync"/> completes.</summary>
    int SchemaVersion { get; }

    /// <summary>Total non-deleted quote rows. Available after <see cref="InitialiseAsync"/> completes.</summary>
    int QuoteCount { get; }

    /// <summary>Total non-deleted source rows. Available after <see cref="InitialiseAsync"/> completes.</summary>
    int SourceCount { get; }

    /// <summary>Total non-deleted character rows. Available after <see cref="InitialiseAsync"/> completes.</summary>
    int CharacterCount { get; }

    /// <summary>Total non-deleted people rows. Available after <see cref="InitialiseAsync"/> completes.</summary>
    int PeopleCount { get; }

    /// <summary>Ensures WAL mode is active, applies any pending schema migrations, and seeds the database from <c>quotes.json</c> if empty.</summary>
    Task InitialiseAsync();
}

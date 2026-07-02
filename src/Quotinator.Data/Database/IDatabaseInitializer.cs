using Quotinator.Data.Import;

namespace Quotinator.Data.Database;

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

    /// <summary>
    /// Description of any migration applied at startup (e.g. <c>"v2 → v3"</c>), or <c>null</c> if
    /// the schema was already up to date. Available after <see cref="InitialiseAsync"/> completes.
    /// </summary>
    string? MigrationApplied { get; }

    /// <summary>
    /// Duplicate records encountered during the last seeding operation.
    /// Populated after <see cref="InitialiseAsync"/>, <see cref="ReseedAsync"/>, or <see cref="ResetAsync"/> completes.
    /// Empty on a fresh database with no cross-file conflicts.
    /// </summary>
    IReadOnlyList<SeedDuplicateRecord> LastSeedDuplicates { get; }

    /// <summary>Ensures WAL mode is active, applies any pending schema migrations, and seeds the database from source files if empty.</summary>
    Task InitialiseAsync();

    /// <summary>Clears all data tables and reimports from all configured source files. Schema migration history is preserved. Updates the row-count properties when done.</summary>
    Task ReseedAsync();

    /// <summary>
    /// Clears all data tables, reapplies all migrations, then reimports from all configured source files.
    /// Updates the row-count properties when done. <c>AuditEntries</c> always survives a reset — it is
    /// deliberately excluded from the table wipe, and is cleared only via its own admin endpoint.
    /// </summary>
    /// <param name="preserveSchemaVersion">
    /// When <c>true</c>, existing schema migration history is left untouched instead of being cleared
    /// and replayed from scratch. Defaults to <c>false</c>, matching the historical behaviour.
    /// </param>
    Task ResetAsync(bool preserveSchemaVersion = false);

    /// <summary>
    /// Scans all configured source files without touching the database and returns a preview of what a
    /// full import would do — file quote counts and any cross-file duplicate quote IDs.
    /// </summary>
    Task<SeedPreviewResult> PreviewSeedAsync();
}

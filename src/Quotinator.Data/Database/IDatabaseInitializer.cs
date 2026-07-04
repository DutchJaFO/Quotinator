using Quotinator.Data.Import;

namespace Quotinator.Data.Database;

/// <summary>Initialises the database schema and seed data at application startup.</summary>
public interface IDatabaseInitializer
{
    /// <summary>
    /// The consuming project's own schema version applied at startup — what operators track
    /// release-over-release. Available after <see cref="InitialiseAsync"/> completes.
    /// </summary>
    int SchemaVersion { get; }

    /// <summary>
    /// Quotinator.Data's own internal schema version (its own infrastructure tables, e.g.
    /// <c>System_AuditEntries</c>) — tracked independently of <see cref="SchemaVersion"/> so the
    /// consuming project's version numbering stays stable regardless of Data's own migration
    /// count. Available after <see cref="InitialiseAsync"/> completes.
    /// </summary>
    int DataSchemaVersion { get; }

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
    /// <param name="forceSourceRefresh">
    /// When <c>true</c>, bypasses the auto-update TTL check for every manifest entry with a
    /// <c>downloadUrl</c>, refreshing all of them from the network regardless of freshness. Has no
    /// effect when <c>Quotinator__AutoUpdateSources</c> is <c>false</c> — an explicit no-network
    /// declaration is never overridden by a force flag. Defaults to <c>false</c>.
    /// </param>
    Task ReseedAsync(bool forceSourceRefresh = false);

    /// <summary>
    /// Clears all data tables, reapplies all migrations, then reimports from all configured source files.
    /// Updates the row-count properties when done. <c>AuditEntries</c> always survives a reset — it is
    /// deliberately excluded from the table wipe, and is cleared only via its own admin endpoint.
    /// </summary>
    /// <param name="preserveSchemaVersion">
    /// When <c>true</c>, existing schema migration history is left untouched instead of being cleared
    /// and replayed from scratch. Defaults to <c>false</c>, matching the historical behaviour.
    /// </param>
    /// <param name="forceSourceRefresh">Same meaning as <see cref="ReseedAsync"/>'s parameter of the same name.</param>
    Task ResetAsync(bool preserveSchemaVersion = false, bool forceSourceRefresh = false);

    /// <summary>
    /// Scans all configured source files without touching the database and returns a preview of what a
    /// full import would do — file quote counts and any cross-file duplicate quote IDs.
    /// </summary>
    Task<SeedPreviewResult> PreviewSeedAsync();

    /// <summary>
    /// Refreshes the download cache for every configured source that declares a
    /// <c>downloadUrl</c>/<c>github</c>, without touching the database or reimporting any data —
    /// the reimport itself only happens on the next reseed/reset/startup. Has no effect when the
    /// auto-update mechanism is disabled entirely.
    /// </summary>
    /// <param name="force">When <c>true</c>, bypasses the TTL check for every entry, refreshing all of them regardless of freshness.</param>
    Task<SourceCacheResolution> RefreshSourcesAsync(bool force = false);
}

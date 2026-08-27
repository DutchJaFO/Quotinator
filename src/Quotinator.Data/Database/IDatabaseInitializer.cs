using Quotinator.Data.Enums;
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

    /// <summary>Total non-deleted series rows (#221). Updated by <see cref="InitialiseAsync"/>, <see cref="ReseedAsync"/>, and <see cref="ResetAsync"/>.</summary>
    int SeriesCount { get; }

    /// <summary>Total non-deleted universe rows (#221). Updated by <see cref="InitialiseAsync"/>, <see cref="ReseedAsync"/>, and <see cref="ResetAsync"/>.</summary>
    int UniverseCount { get; }

    /// <summary>Total non-deleted stage direction rows (#221). Updated by <see cref="InitialiseAsync"/>, <see cref="ReseedAsync"/>, and <see cref="ResetAsync"/>.</summary>
    int StageDirectionCount { get; }

    /// <summary>Total non-deleted sound cue rows (#221). Updated by <see cref="InitialiseAsync"/>, <see cref="ReseedAsync"/>, and <see cref="ResetAsync"/>.</summary>
    int SoundCueCount { get; }

    /// <summary>Total non-deleted conversation rows (#221). Updated by <see cref="InitialiseAsync"/>, <see cref="ReseedAsync"/>, and <see cref="ResetAsync"/>.</summary>
    int ConversationCount { get; }

    /// <summary>
    /// Description of any migration applied at startup (e.g. <c>"v2 → v3"</c>), or <c>null</c> if
    /// the schema was already up to date. Available after <see cref="InitialiseAsync"/> completes.
    /// </summary>
    string? MigrationApplied { get; }

    /// <summary>
    /// <c>true</c> when the database's recorded Data or Consumer schema version exceeds this build's
    /// own known migration count (#289) — the state a migration squash produces on a database that
    /// already applied the pre-squash migrations. The schema itself is treated as complete (nothing is
    /// replayed), but the recorded version is stale relative to this build; an explicit database Reset
    /// resolves the mismatch. Available after <see cref="InitialiseAsync"/> completes.
    /// </summary>
    bool SchemaVersionOvershootDetected { get; }

    /// <summary>
    /// One <see cref="FileImportReport"/> per source file processed during the last seeding
    /// operation, in the order the files were processed (#221). Populated after
    /// <see cref="InitialiseAsync"/>, <see cref="ReseedAsync"/>, or <see cref="ResetAsync"/> completes.
    /// Empty on a fresh database with no configured source files.
    /// </summary>
    IReadOnlyList<FileImportReport> LastSeedReport { get; }

    /// <summary>
    /// Whether a backup can be taken right now, and if not, which obstacle is in the way (#348).
    /// <para>
    /// Cheap and read-mostly — it inspects storage headroom and whether the destination can be written,
    /// never database content — so a caller can ask before acting rather than discovering the answer by
    /// failing. It cannot see every obstacle: an unreadable source only reveals itself to an actual
    /// attempt, and the state can change between checking and acting, which is why exceptions are still
    /// handled around the attempt itself.
    /// </para>
    /// </summary>
    /// <returns><see cref="BackupOutcome.Succeeded"/> when a backup can be taken; otherwise the obstacle.</returns>
    BackupOutcome CheckBackupReadiness();

    /// <summary>Ensures WAL mode is active, applies any pending schema migrations, and seeds the database from source files if empty.</summary>
    /// <returns>
    /// Whether initialisation completed, and if not, which backup obstacle stopped it. A backup that
    /// cannot be taken refuses rather than proceeding unprotected: the schema change would then be
    /// unrecoverable, which is the outcome the backup exists to prevent.
    /// </returns>
    Task<DatabaseOperationResult> InitialiseAsync();

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
    /// <param name="allowNoBackup">
    /// When <c>true</c>, proceeds even though no backup could be taken. Two things at once: the caller
    /// accepts responsibility for there being no restore point, <em>and</em> asserts the action can
    /// complete without one. Never a default, and the skip is recorded in the log and the audit trail so
    /// nobody later hunts for a backup that was never made.
    /// </param>
    /// <returns>
    /// Whether the reset ran, and if not, which backup obstacle refused it. Refusing is a result rather
    /// than an exception: a full backup folder is an ordinary operating condition with remedies, not an
    /// unforeseen fault.
    /// </returns>
    Task<DatabaseOperationResult> ResetAsync(
        bool preserveSchemaVersion = false, bool forceSourceRefresh = false, bool allowNoBackup = false);

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

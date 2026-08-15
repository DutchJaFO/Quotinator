using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Quotinator.Data.Connections;
using Quotinator.Data.Import;
using Quotinator.Data.Logging;
using Quotinator.Data.Models;
using Quotinator.Data.Paths;
using Quotinator.Data.Queries;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Database;

/// <summary>
/// Runs WAL setup and schema migrations. Seeding behaviour is provided by subclasses via the
/// protected virtual hooks <see cref="OnInitialisedAsync"/>, <see cref="OnReseedAsync"/>, and
/// <see cref="OnResetAsync"/>. The base implementations of those hooks are no-ops.
/// </summary>
/// <remarks>Initialises the instance with connection factory, options, and ordered schema migrations.</remarks>
/// <param name="factory">Factory used to open SQLite connections.</param>
/// <param name="options">Database file paths and settings.</param>
/// <param name="migrations">Ordered, append-only list of the consuming project's own schema migrations to apply. Always applied after Quotinator.Data's own migrations.</param>
/// <param name="auditWriter">Writes audit entries for reseed and reset operations.</param>
/// <param name="callerContext">Provides the agent identifier for audit entries.</param>
/// <param name="logger">Logger for startup diagnostics.</param>
/// <param name="baseline">Optional consolidated DDL for the consuming project's own schema, used to create a genuinely fresh database in one step instead of replaying <paramref name="migrations"/>. When omitted, a fresh database always takes the full incremental path.</param>
/// <param name="diskSpaceProvider">Reports real available disk space for the backup pre-flight check (#277). Defaults to a real <see cref="Database.DiskSpaceProvider"/> when omitted — deliberately trailing after <paramref name="baseline"/> so existing callers that stop there are unaffected.</param>
public class DatabaseInitializer(
    IDbConnectionFactory factory,
    DatabaseOptions options,
    IReadOnlyList<SchemaMigration> migrations,
    IAuditEntryWriter auditWriter,
    ICallerContext callerContext,
    ILogger<DatabaseInitializer> logger,
    SchemaBaseline? baseline = null,
    IDiskSpaceProvider? diskSpaceProvider = null) : IDatabaseInitializer
{
    // Optional trailing param defaulting to a real instance rather than a required DI-registered
    // dependency: the ~17 existing call sites across the codebase (production and test) construct
    // this type positionally up to baseline and have nothing to do with the storage pre-flight check
    // (#277) — forcing them all to thread a new required parameter through would be pure churn.
    // Program.cs's own real wiring still resolves IDiskSpaceProvider from DI and passes it explicitly.
    private readonly IDiskSpaceProvider _diskSpaceProvider = diskSpaceProvider ?? new DiskSpaceProvider();

    // Quotinator.Data's own migrations, for its own tables (Audit_Entry/Audit_Change/Import_Conflict/
    // Import_Action/Import_SourceFileOverride currently; any future Import_/Audit_/System_-prefixed
    // table Quotinator.Data itself defines). Never passed through the constructor — Quotinator.Data
    // owns and maintains these scripts itself, and they always apply before any consumer-supplied
    // migration, tracked in their own System_SchemaVersion table, independent of the consumer's own
    // System_ConsumerSchemaVersion count. ImportBatches/Import_Batch is NOT here despite ADR 015
    // classifying it as Data-owned — see DomainPrefixRenameMigrations' own remarks for why that
    // specific rename must instead live in Quotinator.Core's migration list (#254).
    // #155: version 2 consolidates every Data-owned migration shipped since v1.7.2's single frozen
    // migration (version 1) — see DataConsolidatedMigrations.SinceV172 for the full reasoning.
    // #289: version 3 consolidates every Data-owned migration added since v1.8.2 (the former versions
    // 3-8: two AppliedPolicy CHECK constraints, the domain-prefix rename, FileResource's tables, its
    // Origin generalization, and System_Notification) — see DataConsolidatedMigrations.SinceV182.
    // None of the former versions 3-8 had shipped in a tagged release, but this project's own local
    // dev database had already applied all of them via each issue's own T1 pass earlier in this
    // milestone (confirmed live before squashing, not assumed) — per ADR 015's revision (from #254),
    // "unreleased" is not the right test for whether a migration is safe to edit; the real test is
    // whether any real database, including a developer's own, has already applied it. The squash was
    // done anyway, by deliberate developer decision, with the local dev database being reset as part
    // of this same work — see #289's plan doc. ApplyMigrationsAsync's own schema-version-overshoot
    // detection is the safety net for every other database (a second developer's machine, a CI cache)
    // that may be in the same already-migrated state and isn't being reset alongside this one.
    private static readonly IReadOnlyList<SchemaMigration> DataOwnedMigrations =
    [
        new SchemaMigration { Version = 1, Sql = AuditMigrations.CreateAuditEntriesTable },
        new SchemaMigration { Version = 2, Sql = DataConsolidatedMigrations.SinceV172 },
        new SchemaMigration { Version = 3, Sql = DataConsolidatedMigrations.SinceV182 },
        // #81: System_AppVersion tracks the last app version that completed a healthy startup, read
        // before migrations run on the following boot so the what's-new notification producer can walk
        // every release missed since then, not just the one currently running.
        new SchemaMigration { Version = 4, Sql = AppVersionMigrations.CreateAppVersionTable },
        // #312: System_Notification gains a Title/Body split, a typed Metadata payload, and an
        // AppVersionId provenance reference — the foundation the milestone's remaining producers and
        // its richer rendering both build on.
        new SchemaMigration { Version = 5, Sql = NotificationSchemaMigrations.SplitMessageAndAddMetadata },
        // #312: System_AppVersion becomes an append-only Application+Version history, so a
        // notification's provenance reference stays frozen instead of re-pointing on upgrade.
        new SchemaMigration { Version = 6, Sql = AppVersionHistoryMigrations.AddApplicationColumn },
        // #312: and gains an explicit recording-order counter, since neither DateCreated (second
        // resolution) nor SQLite's implicit rowid is a trustworthy answer to "which version ran last".
        new SchemaMigration { Version = 7, Sql = AppVersionHistoryMigrations.AddSequenceNumberColumn },
    ];

    // Data's own baseline fragment — creates every Data-owned table directly under its final,
    // domain-prefixed name for a genuinely fresh database, skipping the historical
    // create-then-rename-then-RecordBase-migrate dance entirely. All tables carry RecordBase's
    // DateCreated/DateModified/DateDeleted/IsDeleted per ADR 002. Kept in sync with
    // DataOwnedMigrations by this project's own schema-drift test.
    private const string DataBaselineSql = """
        CREATE TABLE IF NOT EXISTS Import_Batch (
            Id             TEXT    PRIMARY KEY,
            Name           TEXT    NOT NULL,
            Type           TEXT    NOT NULL CHECK (Type IN ('Seed', 'Import', 'System', 'UserSeed')),
            Url            TEXT,
            ImportedAt     TEXT    NOT NULL,
            ImportedById   TEXT,
            RecordCount    INTEGER NOT NULL DEFAULT 0,
            DateCreated    TEXT    NOT NULL,
            DateModified   TEXT,
            DateDeleted    TEXT,
            IsDeleted      INTEGER NOT NULL DEFAULT 0,
            ConflictPolicy TEXT    NOT NULL DEFAULT 'Skip'
                           CHECK (ConflictPolicy IN ('Skip', 'NewestWins', 'MergeOurs', 'MergeTheirs', 'Review')),
            Status         TEXT    NOT NULL DEFAULT 'Applied'
                           CHECK (Status IN ('Staged', 'Applied', 'Discarded')),
            AppliedAt      TEXT
        );

        CREATE TABLE IF NOT EXISTS Audit_Entry (
            Id           TEXT    NOT NULL PRIMARY KEY,
            TableName    TEXT    NOT NULL,
            RecordId     TEXT,
            Operation    TEXT    NOT NULL,
            Agent        TEXT,
            PerformedAt  TEXT    NOT NULL,
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS IX_Audit_Entry_TableName_RecordId ON Audit_Entry (TableName, RecordId);
        CREATE INDEX IF NOT EXISTS IX_Audit_Entry_PerformedAt ON Audit_Entry (PerformedAt);

        CREATE TABLE IF NOT EXISTS Import_Conflict (
            Id              TEXT    NOT NULL PRIMARY KEY,
            BatchId         TEXT    NOT NULL,
            EntityType      TEXT    NOT NULL,
            EntityId        TEXT,
            ExistingValue   TEXT,
            IncomingValue   TEXT,
            AppliedPolicy   TEXT
                            CHECK (AppliedPolicy IS NULL OR AppliedPolicy IN ('Skip', 'NewestWins', 'MergeOurs', 'MergeTheirs', 'Review')),
            Status          TEXT    NOT NULL
                            CHECK (Status IN ('Pending', 'Decided', 'Resolved')),
            MergedFields    TEXT,
            DetectedAt      TEXT    NOT NULL,
            ResolvedAt      TEXT,
            DateCreated     TEXT    NOT NULL,
            DateModified    TEXT,
            DateDeleted     TEXT,
            IsDeleted       INTEGER NOT NULL DEFAULT 0,
            ExistingBatchId TEXT
        );
        CREATE INDEX IF NOT EXISTS IX_Import_Conflict_BatchId ON Import_Conflict (BatchId);
        CREATE INDEX IF NOT EXISTS IX_Import_Conflict_Status ON Import_Conflict (Status);

        CREATE TABLE IF NOT EXISTS Import_Action (
            Id                 TEXT    NOT NULL PRIMARY KEY,
            BatchId            TEXT    NOT NULL,
            ActionType         TEXT    NOT NULL
                               CHECK (ActionType IN ('Add', 'Modify')),
            EntityType         TEXT    NOT NULL,
            EntityId           TEXT    NOT NULL,
            ExistingBatchId    TEXT,
            ExistingValue      TEXT,
            IncomingValue      TEXT    NOT NULL,
            AppliedPolicy      TEXT
                               CHECK (AppliedPolicy IS NULL OR AppliedPolicy IN ('Skip', 'NewestWins', 'MergeOurs', 'MergeTheirs', 'Review')),
            Status             TEXT    NOT NULL
                               CHECK (Status IN ('Pending', 'Decided', 'Applied', 'Discarded', 'Blocked', 'Stale')),
            MergedFields       TEXT,
            MarkCompletenessAs TEXT
                               CHECK (MarkCompletenessAs IS NULL OR MarkCompletenessAs IN ('Incomplete', 'NeedsReview', 'Complete')),
            DetectedAt         TEXT    NOT NULL,
            AppliedAt          TEXT,
            DiscardedAt        TEXT,
            DateCreated        TEXT    NOT NULL,
            DateModified       TEXT,
            DateDeleted        TEXT,
            IsDeleted          INTEGER NOT NULL DEFAULT 0,
            OriginalDecision   TEXT
        );
        CREATE INDEX IF NOT EXISTS IX_Import_Action_BatchId ON Import_Action (BatchId);
        CREATE INDEX IF NOT EXISTS IX_Import_Action_Status ON Import_Action (Status);

        CREATE TABLE IF NOT EXISTS Import_SourceFileOverride (
            Id            TEXT    NOT NULL PRIMARY KEY,
            FileName      TEXT    NOT NULL,
            Origin        TEXT    NOT NULL
                          CHECK (Origin IN ('Bundled', 'UserImports')),
            ContentHash   TEXT    NOT NULL,
            SourceBatchId TEXT,
            DateCreated   TEXT    NOT NULL,
            DateModified  TEXT,
            DateDeleted   TEXT,
            IsDeleted     INTEGER NOT NULL DEFAULT 0
        );
        CREATE UNIQUE INDEX IF NOT EXISTS UX_Import_SourceFileOverride_FileName_Origin
            ON Import_SourceFileOverride (FileName, Origin) WHERE IsDeleted = 0;

        CREATE TABLE IF NOT EXISTS Import_FileResource (
            Id                      TEXT    NOT NULL PRIMARY KEY,
            FileName                TEXT    NOT NULL,
            OriginalFolderPath      TEXT,
            Origin                  TEXT    NOT NULL
                                    CHECK (Origin IN ('System', 'User', 'Upload')),
            HomeDirectoryKey        TEXT,
            ContentHash             TEXT    NOT NULL,
            LineEnding              TEXT    NOT NULL
                                    CHECK (LineEnding IN ('LF', 'CRLF', 'CR')),
            EndsWithTrailingNewline INTEGER NOT NULL,
            Converter               TEXT,
            ConverterOptions        TEXT,
            FirstSeenAtUtc          TEXT    NOT NULL,
            LastSeenAtUtc           TEXT    NOT NULL,
            DateCreated             TEXT    NOT NULL,
            DateModified            TEXT,
            DateDeleted             TEXT,
            IsDeleted               INTEGER NOT NULL DEFAULT 0
        );
        CREATE UNIQUE INDEX IF NOT EXISTS UX_Import_FileResource_ContentHash ON Import_FileResource (ContentHash);
        CREATE INDEX IF NOT EXISTS IX_Import_FileResource_FileName ON Import_FileResource (FileName);

        CREATE TABLE IF NOT EXISTS Import_FileResourceLine (
            Id             TEXT    NOT NULL PRIMARY KEY,
            FileResourceId TEXT    NOT NULL REFERENCES Import_FileResource(Id) ON DELETE CASCADE,
            LineNumber     INTEGER NOT NULL,
            Text           TEXT    NOT NULL,
            DateCreated    TEXT    NOT NULL,
            DateModified   TEXT,
            DateDeleted    TEXT,
            IsDeleted      INTEGER NOT NULL DEFAULT 0,
            UNIQUE (FileResourceId, LineNumber)
        );

        CREATE TABLE IF NOT EXISTS Import_FileResourceBatch (
            Id             TEXT    NOT NULL PRIMARY KEY,
            FileResourceId TEXT    NOT NULL REFERENCES Import_FileResource(Id) ON DELETE CASCADE,
            ImportBatchId  TEXT    NOT NULL REFERENCES Import_Batch(Id),
            ImportedAt     TEXT    NOT NULL,
            DateCreated    TEXT    NOT NULL,
            DateModified   TEXT,
            DateDeleted    TEXT,
            IsDeleted      INTEGER NOT NULL DEFAULT 0,
            UNIQUE (FileResourceId, ImportBatchId)
        );

        CREATE TABLE IF NOT EXISTS Audit_Change (
            Id               TEXT NOT NULL PRIMARY KEY,
            EntityType       TEXT NOT NULL,
            EntityId         TEXT NOT NULL,
            InitiatedByType  TEXT NOT NULL
                             CHECK (InitiatedByType IN ('Seed','Import','WriteEndpoint','Enrichment')),
            InitiatedById    TEXT,
            Action           TEXT NOT NULL
                             CHECK (Action IN ('Created','Modified','SoftDelete','HardDelete')),
            Field            TEXT,
            OldValue         TEXT,
            NewValue         TEXT,
            OccurredAt       TEXT NOT NULL,
            DateCreated      TEXT NOT NULL,
            DateModified     TEXT,
            DateDeleted      TEXT,
            IsDeleted        INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS IX_Audit_Change_Entity ON Audit_Change (EntityType, EntityId, OccurredAt DESC);

        -- Column order below is deliberately not the "tidy" order: Title/Metadata/MetadataKind/
        -- AppVersionId trail after IsDeleted because #312's migration adds them via ALTER TABLE ADD
        -- COLUMN, which always appends. The schema-drift parity test compares PRAGMA table_info
        -- including each column's ordinal, so the baseline has to reproduce the incremental path's
        -- real result, not a prettier one. The milestone's own end-of-cycle migration consolidation
        -- is what restores a clean ordering.
        CREATE TABLE IF NOT EXISTS System_Notification (
            Id                TEXT    NOT NULL PRIMARY KEY,
            Type              TEXT    NOT NULL
                              CHECK (Type IN ('Information', 'Warning', 'Error', 'Success', 'ActionRequired')),
            Body              TEXT    NOT NULL,
            ExpiresAt         TEXT,
            IsDismissed       INTEGER NOT NULL DEFAULT 0,
            DismissedAt       TEXT,
            DismissTriggerKey TEXT
                              CHECK (DismissTriggerKey IS NULL OR DismissTriggerKey IN ('DatabaseReset')),
            DateCreated       TEXT    NOT NULL,
            DateModified      TEXT,
            DateDeleted       TEXT,
            IsDeleted         INTEGER NOT NULL DEFAULT 0,
            Title             TEXT,
            Metadata          TEXT,
            MetadataKind      TEXT
                              CHECK (MetadataKind IS NULL OR MetadataKind IN ('Announcement', 'SchemaVersionOvershoot', 'WhatsNew')),
            AppVersionId      TEXT    REFERENCES System_AppVersion(Id)
        );
        CREATE INDEX IF NOT EXISTS IX_System_Notification_Active ON System_Notification (IsDismissed, IsDeleted, ExpiresAt);
        CREATE INDEX IF NOT EXISTS IX_System_Notification_DismissTriggerKey ON System_Notification (DismissTriggerKey);

        -- Application and SequenceNumber trail after IsDeleted for the same reason System_Notification's
        -- new columns do: #312 adds them via ALTER TABLE ADD COLUMN, which appends, and the schema-drift
        -- parity test compares ordinals. See the note above System_Notification.
        CREATE TABLE IF NOT EXISTS System_AppVersion (
            Id             TEXT NOT NULL PRIMARY KEY,
            Version        TEXT NOT NULL,
            DateCreated    TEXT NOT NULL,
            DateModified   TEXT,
            DateDeleted    TEXT,
            IsDeleted      INTEGER NOT NULL DEFAULT 0,
            Application    TEXT,
            SequenceNumber INTEGER NOT NULL DEFAULT 0
        );
        CREATE UNIQUE INDEX IF NOT EXISTS UX_System_AppVersion_Application_Version
            ON System_AppVersion (Application, Version);
        CREATE UNIQUE INDEX IF NOT EXISTS UX_System_AppVersion_SequenceNumber
            ON System_AppVersion (SequenceNumber);
        """;

    private readonly IDbConnectionFactory _factory = factory;
    private readonly DatabaseOptions _options = options;
    private readonly IReadOnlyList<SchemaMigration> _consumerMigrations = migrations;
    private readonly SchemaBaseline? _consumerBaseline = baseline;

    /// <summary>Logger available to this class and subclasses.</summary>
    protected readonly ILogger Logger = logger;

    /// <summary>Audit writer available to subclasses for recording reseed and reset operations.</summary>
    protected readonly IAuditEntryWriter AuditWriter = auditWriter;

    /// <summary>Caller context available to subclasses for populating audit entries.</summary>
    protected readonly ICallerContext CallerContext = callerContext;

    /// <inheritdoc/>
    public int SchemaVersion { get; protected set; }

    /// <inheritdoc/>
    public int DataSchemaVersion { get; protected set; }

    /// <inheritdoc/>
    public int QuoteCount { get; protected set; }

    /// <inheritdoc/>
    public int SourceCount { get; protected set; }

    /// <inheritdoc/>
    public int CharacterCount { get; protected set; }

    /// <inheritdoc/>
    public int PeopleCount { get; protected set; }

    /// <inheritdoc/>
    public int SeriesCount { get; protected set; }

    /// <inheritdoc/>
    public int UniverseCount { get; protected set; }

    /// <inheritdoc/>
    public int StageDirectionCount { get; protected set; }

    /// <inheritdoc/>
    public int SoundCueCount { get; protected set; }

    /// <inheritdoc/>
    public int ConversationCount { get; protected set; }

    /// <inheritdoc/>
    public string? MigrationApplied { get; protected set; }

    /// <inheritdoc/>
    public bool SchemaVersionOvershootDetected { get; protected set; }

    /// <inheritdoc/>
    public IReadOnlyList<FileImportReport> LastSeedReport { get; protected set; } = [];

    // Guards against concurrent seeding when multiple WebApplicationFactory instances start in
    // the same process (e.g. parallel MSTest runs). Each waiter re-checks COUNT(*) after
    // acquiring the lock and skips seeding if the previous holder already populated the DB.
    private static readonly SemaphoreSlim SeedLock = new(1, 1);

    /// <summary>A semaphore that subclasses must acquire before performing seeding operations, to prevent concurrent seed runs.</summary>
    protected static SemaphoreSlim SharedSeedLock => SeedLock;

    /// <inheritdoc/>
    public async Task InitialiseAsync()
    {
        MigrateFilenameIfNeeded();

        using SqliteConnection connection = (SqliteConnection)_factory.CreateConnection();
        await connection.OpenAsync();

        EnableWal(connection);
        bool tookBaselinePath = await ApplyMigrationsAsync(connection);
        await RunInitialisedHookAsync(connection, tookBaselinePath);
    }

    /// <summary>
    /// Test-only entry point that mirrors <see cref="InitialiseAsync"/> but can force the
    /// incremental migration path even on an empty database, bypassing the baseline short-circuit.
    /// Used by schema-drift tests to produce a "pure incremental" comparison database.
    /// </summary>
    internal async Task InitialiseForTestingAsync(bool forceIncremental)
    {
        MigrateFilenameIfNeeded();

        using SqliteConnection connection = (SqliteConnection)_factory.CreateConnection();
        await connection.OpenAsync();

        EnableWal(connection);
        bool tookBaselinePath = await ApplyMigrationsAsync(connection, forceIncremental);
        await RunInitialisedHookAsync(connection, tookBaselinePath);
    }

    /// <summary>
    /// Called after migrations are applied. Override to perform domain-specific seeding and
    /// statistics collection. The base implementation is a no-op.
    /// </summary>
    protected virtual Task OnInitialisedAsync(SqliteConnection connection) => Task.CompletedTask;

    /// <summary>
    /// Reports whether <see cref="OnInitialisedAsync"/> would perform genuine seeding work if called
    /// right now, mirroring <see cref="ApplyMigrationsAsync"/>'s own <c>dataPending</c>/
    /// <c>consumerPending</c> real-work gate for the migration step (#277). The base class has no
    /// domain knowledge of what a subclass actually seeds, so the base implementation conservatively
    /// returns <c>true</c> (always back up) — override with a real, cheap count-check once domain
    /// tables exist to check.
    /// </summary>
    protected virtual Task<bool> HasPendingContentSeedAsync(SqliteConnection connection) => Task.FromResult(true);

    // A failure determining whether content-seed has pending work is itself strong evidence something
    // is structurally wrong (e.g. a domain table was dropped or renamed outside a normal migration) —
    // exactly the case a pre-seed backup exists to protect against. Treating the determination itself
    // as fail-open (skip backup on any exception) would remove that protection at the one moment it
    // matters most; assume "pending" instead, so a backup is still taken before the same query is
    // attempted again for real inside OnInitialisedAsync.
    private async Task<bool> SafeHasPendingContentSeedAsync(SqliteConnection connection)
    {
        try
        {
            return await HasPendingContentSeedAsync(connection);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[Database - Init] failed to determine whether content-seed has pending work — assuming yes, taking a backup before proceeding.");
            return true;
        }
    }

    // Startup/reset collapses into three flows — normal startup, fresh install, and Reset — and every
    // action within them reduces to the same shape: can we perform it → back up → execute. The
    // migration phase already gates its own backup on dataPending/consumerPending (see
    // ApplyMigrationsAsync); this mirrors that for the content-seed step via HasPendingContentSeedAsync
    // instead of inferring readiness from a different step's own flag (tookBaselinePath/
    // MigrationApplied) — a flag-based gate was tried first and found to miss the startup immediately
    // following a Reset, where MigrationApplied stays null (Reset sets schema-version counters
    // directly via the baseline path) even though content-seed genuinely has real work to do. A
    // genuinely fresh (baseline) database has nothing to lose and is still skipped outright.
    private async Task RunInitialisedHookAsync(SqliteConnection connection, bool tookBaselinePath)
    {
        if (tookBaselinePath || !await SafeHasPendingContentSeedAsync(connection))
        {
            await OnInitialisedAsync(connection);
            return;
        }

        string? backupPath = CreateBackup(connection, Math.Max(DataSchemaVersion, SchemaVersion));
        try
        {
            await OnInitialisedAsync(connection);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[Database - Init] seeding failed — restoring pre-seed backup, database left unchanged...");
            if (backupPath is not null)
            {
                RestoreBackup(connection, backupPath);
                Logger.LogInformation("[Database - Init] pre-seed backup restored.");
            }
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task ReseedAsync(bool forceSourceRefresh = false)
    {
        using SqliteConnection connection = (SqliteConnection)_factory.CreateConnection();
        await connection.OpenAsync();
        await OnReseedAsync(connection, forceSourceRefresh);
    }

    /// <summary>
    /// Called by <see cref="ReseedAsync"/>. Override to replace the default no-op with a
    /// domain-specific reseed implementation. Base implementation does nothing.
    /// </summary>
    protected virtual Task OnReseedAsync(SqliteConnection connection, bool forceSourceRefresh) => Task.CompletedTask;

    /// <inheritdoc/>
    public async Task ResetAsync(bool preserveSchemaVersion = false, bool forceSourceRefresh = false)
    {
        using SqliteConnection connection = (SqliteConnection)_factory.CreateConnection();
        await connection.OpenAsync();
        await OnResetAsync(connection, preserveSchemaVersion, forceSourceRefresh);
    }

    /// <summary>
    /// Called by <see cref="ResetAsync"/>. Override to replace the default no-op with a
    /// domain-specific reset implementation. Base implementation does nothing.
    /// </summary>
    protected virtual Task OnResetAsync(SqliteConnection connection, bool preserveSchemaVersion, bool forceSourceRefresh) => Task.CompletedTask;

    /// <summary>
    /// Called unconditionally after a genuinely fresh database is created via the baseline path
    /// (<see cref="ApplyBaselineAsync"/>) and after every <see cref="DropAndRebuildAsync"/> call —
    /// i.e. after both "first ever install" and "any reset," the two moments a database can be
    /// missing content it structurally needs to function. Override to populate designated system
    /// tables (vital, non-optional reference/configuration content) from whatever source the
    /// subclass chooses. This is deliberately separate from <see cref="OnReseedAsync"/>/
    /// <see cref="OnResetAsync"/>'s own bundled/user content reseeding, which is optional domain
    /// data and — per #156 — is never triggered automatically by a Reset. Base implementation does
    /// nothing; there is no system content to seed until a subclass defines some (#156).
    /// </summary>
    protected virtual Task SeedSystemContentAsync(SqliteConnection connection) => Task.CompletedTask;

    /// <inheritdoc/>
    public virtual Task<SeedPreviewResult> PreviewSeedAsync()
        => Task.FromResult(new SeedPreviewResult([], []));

    /// <inheritdoc/>
    public virtual Task<SourceCacheResolution> RefreshSourcesAsync(bool force = false)
        => Task.FromResult(new SourceCacheResolution([], []));

    // -------------------------------------------------------------------------
    #region Protected utilities for subclasses

    /// <summary>
    /// Drops the entire database — every table, with no protected/excluded set of any kind — and
    /// recreates it from scratch via the same baseline path a fresh install uses (#156). Reset is a
    /// full wipe: <c>Audit_Entry</c> and every other <c>Import_</c>/<c>Audit_</c>/<c>System_</c>-prefixed
    /// table Quotinator.Data itself owns is dropped along with the consumer's own tables and does
    /// not survive — a deliberate tradeoff (see ADR 014); an operator who wants to keep audit-trail
    /// data retrieves it beforehand via the admin audit export endpoint (#249). When
    /// <paramref name="preserveSchemaVersion"/> is <c>true</c>, both <c>System_SchemaVersion</c>'s
    /// and <c>System_ConsumerSchemaVersion</c>'s granular per-version rows are snapshotted first and
    /// restored afterward, in place of the single collapsed row the baseline path would otherwise
    /// leave — preserving history granularity symmetrically for both counters now that both are
    /// wiped, not just the consumer's. <see cref="SeedSystemContentAsync"/> is invoked exactly once
    /// regardless of which path <see cref="ApplyMigrationsAsync"/> takes — once truly empty (which a
    /// full wipe always leaves it), that call already invokes it internally when a baseline is
    /// configured, so this method only calls it directly for the (rare) case where no baseline is
    /// configured and the incremental-replay-from-zero path runs instead. A full backup is always
    /// taken before any destructive step; any failure anywhere in the rebuild restores it and
    /// rethrows, without attempting to interpret what went wrong.
    /// </summary>
    protected async Task DropAndRebuildAsync(SqliteConnection connection, bool preserveSchemaVersion = false)
    {
        List<SystemSchemaVersionRow> savedDataVersions = preserveSchemaVersion
            ? [.. await connection.QueryAsync<SystemSchemaVersionRow>(Sql.Schema.GetAllDataVersions)]
            : [];
        List<SystemSchemaVersionRow> savedConsumerVersions = preserveSchemaVersion
            ? [.. await connection.QueryAsync<SystemSchemaVersionRow>(Sql.Schema.GetAllConsumerVersions)]
            : [];

        string? backupPath = CreateBackup(connection, SchemaVersion);

        try
        {
            await connection.ExecuteAsync("PRAGMA foreign_keys = OFF;");
            await DropAllTablesAsync(connection);
            await connection.ExecuteAsync("PRAGMA foreign_keys = ON;");
            bool tookBaselinePath = await ApplyMigrationsAsync(connection, skipOwnBackup: true);

            if (preserveSchemaVersion)
            {
                await connection.ExecuteAsync(Sql.Schema.DeleteAllDataVersions);
                foreach (SystemSchemaVersionRow row in savedDataVersions)
                    await connection.ExecuteAsync(Sql.Schema.InsertDataVersion, new { v = row.Version, at = row.AppliedAt });

                await connection.ExecuteAsync(Sql.Schema.DeleteAllConsumerVersions);
                foreach (SystemSchemaVersionRow row in savedConsumerVersions)
                    await connection.ExecuteAsync(Sql.Schema.InsertConsumerVersion, new { v = row.Version, at = row.AppliedAt });
            }

            if (!tookBaselinePath)
                await SeedSystemContentAsync(connection);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[Database - Init] reset failed — restoring pre-reset backup, database left unchanged...");
            if (backupPath is not null)
            {
                RestoreBackup(connection, backupPath);
                Logger.LogInformation("[Database - Init] pre-reset backup restored.");
            }
            throw;
        }
    }

    private sealed record SystemSchemaVersionRow(long Version, string AppliedAt);

    /// <summary>Opens a new SQLite connection for use by subclasses.</summary>
    protected SqliteConnection CreateConnection() => (SqliteConnection)_factory.CreateConnection();

    #endregion

    // -------------------------------------------------------------------------
    #region File management

    private void MigrateFilenameIfNeeded()
    {
        string dataDir    = Path.GetDirectoryName(_options.DbPath)!;
        string legacyPath = Path.Combine(dataDir, DataPaths.LegacyDatabaseFile);
        if (!File.Exists(legacyPath) || File.Exists(_options.DbPath)) return;

        if (Logger.IsEnabled(LogLevel.Information))
            Logger.LogLegacyFilenameMigrationStarting(Path.GetFileName(_options.DbPath));
        foreach (string? suffix in new[] { "", "-wal", "-shm" })
        {
            string src = legacyPath + suffix;
            string dst = _options.DbPath + suffix;
            if (!File.Exists(src)) continue;
            if (Logger.IsEnabled(LogLevel.Information))
                Logger.LogMovingLegacyFile(Path.GetFileName(src), Path.GetFileName(dst));
            File.Move(src, dst);
        }
        Logger.LogLegacyFilenameMigrationComplete(_options.DbPath);
    }

    // Storage pre-flight check (#277) — two independent conditions, either enough to skip a backup
    // (warning logged, no exception, caller proceeds without one): a hard budget on how large the
    // BackupsPath folder's own accumulated backups may grow ("never exceed our budget," per explicit
    // developer direction — independent of how much real disk space happens to be free), and a real
    // free-space check via IDiskSpaceProvider (so a genuinely full disk is never written to,
    // regardless of budget headroom). A failure writing the file itself, once both checks pass, is a
    // distinct condition — DatabaseBackupWriteException, not a skip.
    private string? CreateBackup(SqliteConnection connection, int fromVersion)
    {
        long estimatedBytes = File.Exists(_options.DbPath) ? new FileInfo(_options.DbPath).Length : 0L;
        long budgetBytes    = _options.MaxBackupStorageGb * 1_073_741_824L;
        long existingBytes  = Directory.Exists(_options.BackupsPath)
            ? Directory.EnumerateFiles(_options.BackupsPath).Sum(f => new FileInfo(f).Length)
            : 0L;

        if (existingBytes + estimatedBytes > budgetBytes)
        {
            Logger.LogBackupSkippedBudgetExceeded(_options.MaxBackupStorageGb, existingBytes, estimatedBytes);
            return null;
        }

        long availableBytes = _diskSpaceProvider.GetAvailableFreeSpaceBytes(_options.BackupsPath);
        if (availableBytes < estimatedBytes)
        {
            Logger.LogBackupSkippedInsufficientDiskSpace(availableBytes, estimatedBytes);
            return null;
        }

        // #289: millisecond precision, not just seconds — found live when #289's migration squash
        // happened to make two real, distinct backups within the same test (Reset's own backup, then
        // the following InitialiseAsync's) land on the same fromVersion for the first time (previously
        // 6 vs 8, now both 5 after the squash). Second-precision timestamps let two same-version
        // backups within the same second collide on an identical filename — SqliteConnection.
        // BackupDatabase silently overwrites the existing file at that path rather than erroring, so
        // the second backup was never actually taking a distinct new file. Not specific to this one
        // version-number coincidence — any two same-version backups within the same wall-clock second
        // could always have collided this way; milliseconds make that effectively impossible.
        string timestamp  = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff");
        string backupName = $"{Path.GetFileNameWithoutExtension(_options.DbPath)}_v{fromVersion}_{timestamp}Z.db";
        string backupPath = Path.Combine(_options.BackupsPath, backupName);

        try
        {
            Directory.CreateDirectory(_options.BackupsPath);
            Logger.LogBackupStarting(fromVersion, backupPath);
            using SqliteConnection dest = new SqliteConnection($"Data Source={backupPath}");
            dest.Open();
            connection.BackupDatabase(dest);
            Logger.LogBackupComplete();
            return backupPath;
        }
        catch (Exception ex)
        {
            throw new DatabaseBackupWriteException(backupPath, ex);
        }
    }

    // Restores a backup file created by CreateBackup back into the live connection — the reverse
    // direction of the same SQLite online-backup API. Used when a migration attempt fails partway
    // through, so the caller is left with the database exactly as it was before the attempt started
    // rather than a partially-migrated or partially-rebuilt one.
    private static void RestoreBackup(SqliteConnection connection, string backupPath)
    {
        using SqliteConnection source = new SqliteConnection($"Data Source={backupPath}");
        source.Open();
        source.BackupDatabase(connection);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Migrations

    // journal_mode=WAL is persistent (stored in the database file itself), unlike temp_store — see
    // SqliteConnectionFactory.CreateConnection's own StateChange handler for why temp_store=MEMORY is
    // applied there instead, on every connection, rather than duplicated here.
    private static void EnableWal(SqliteConnection connection)
        => connection.Execute("PRAGMA journal_mode=WAL;");

    // One-time bootstrap step, run after both version tables exist (their rows are the insert
    // target) but before either current migration version is read — SchemaVersion predates the
    // numbered migration list entirely, so splitting it can't itself be a numbered migration. A
    // fresh database has no table literally named SchemaVersion, so this is a no-op — a new
    // database is never created under the old name. See #155 for why this splits explicitly by
    // hardcoded version number rather than renaming the table (a bare rename silently skipped
    // Data migrations 2-4 on a real v1.7.2 upgrade, since it copied the legacy counter's raw value
    // straight into System_SchemaVersion).
    private static async Task SplitLegacySchemaVersionIfPresentAsync(SqliteConnection connection)
    {
        int legacyExists = await connection.ExecuteScalarAsync<int>(Sql.Schema.LegacySchemaVersionExists);
        if (legacyExists == 0) return;

        await connection.ExecuteAsync(Sql.Schema.SplitLegacySchemaVersionIntoConsumer);
        await connection.ExecuteAsync(Sql.Schema.SplitLegacySchemaVersionIntoData);
        await connection.ExecuteAsync(Sql.Schema.DropLegacySchemaVersionTable);
    }

    /// <summary>Applies pending migrations (or the baseline for a genuinely fresh database). Returns <c>true</c> when the baseline path was taken — the caller uses this to decide whether a pre-seed backup is worth taking (a freshly-created database has nothing to lose).</summary>
    private async Task<bool> ApplyMigrationsAsync(SqliteConnection connection, bool forceIncremental = false, bool skipOwnBackup = false)
    {
        // Must run before either CreateXVersionTable call below — those would otherwise make every
        // fresh database register as "not empty" on the very next line, permanently disabling the
        // baseline path. A legacy (pre-split) database already has many other tables, so it never
        // reads as empty here regardless of whether the legacy SchemaVersion table has been split yet.
        bool isEmptyDatabase = await connection.ExecuteScalarAsync<int>(Sql.Schema.AnyTableExists) == 0;

        await connection.ExecuteAsync(Sql.Schema.CreateDataVersionTable);
        await connection.ExecuteAsync(Sql.Schema.CreateConsumerVersionTable);
        await SplitLegacySchemaVersionIfPresentAsync(connection);

        if (isEmptyDatabase && !forceIncremental && _consumerBaseline is not null)
        {
            await ApplyBaselineAsync(connection);
            return true;
        }

        int dataCurrent     = await connection.ExecuteScalarAsync<int>(Sql.Schema.GetDataCurrentVersion);
        int consumerCurrent = await connection.ExecuteScalarAsync<int>(Sql.Schema.GetConsumerCurrentVersion);

        // #289: a recorded version higher than this build's own known migration count is only
        // reachable after a migration squash, on a database that already applied the pre-squash
        // migrations — the schema itself is complete (nothing to replay), only the counter is stale
        // relative to this build. Detected here (not treated as a hard failure) so the caller can
        // surface it via a notification instead — see IDatabaseInitializer.SchemaVersionOvershootDetected.
        SchemaVersionOvershootDetected =
            dataCurrent > DataOwnedMigrations.Count || consumerCurrent > _consumerMigrations.Count;

        bool dataPending     = dataCurrent     < DataOwnedMigrations.Count;
        bool consumerPending = consumerCurrent < _consumerMigrations.Count;

        if (!dataPending && !consumerPending)
        {
            DataSchemaVersion = dataCurrent;
            SchemaVersion     = consumerCurrent;
            Logger.LogSchemaUpToDate(dataCurrent, consumerCurrent);
            if (SchemaVersionOvershootDetected)
                Logger.LogSchemaVersionOvershoot(dataCurrent, DataOwnedMigrations.Count, consumerCurrent, _consumerMigrations.Count);
            return false;
        }

        // skipOwnBackup: DropAndRebuildAsync (Reset) already took its own backup before this call —
        // Data's counter is never wiped by Reset, so this condition would otherwise fire pointlessly
        // (a redundant second backup) on every Reset.
        string? backupPath = !skipOwnBackup && (dataCurrent > 0 || consumerCurrent > 0)
            ? CreateBackup(connection, Math.Max(dataCurrent, consumerCurrent))
            : null;

        // Some migrations recreate a table (SQLite has no ALTER ... CHECK) to widen a constraint,
        // which requires dropping a table that other tables still hold live foreign-key references
        // to. Foreign key enforcement must be off for the duration — PRAGMA foreign_keys is a no-op
        // inside a transaction, so it cannot be toggled from within a migration's own SQL text.
        await connection.ExecuteAsync("PRAGMA foreign_keys = OFF;");
        try
        {
            string? dataApplied = await ApplyMigrationPhaseAsync(
                connection, "Data", DataOwnedMigrations, dataCurrent, Sql.Schema.InsertDataVersion);
            // #289: Math.Max, not a bare assignment to DataOwnedMigrations.Count — when this side
            // overshoots while the other side has genuine pending work (so this whole method doesn't
            // take the early "both up to date" return above), ApplyMigrationPhaseAsync writes nothing
            // for this side (current >= migrations.Count), so the true recorded version stays
            // dataCurrent, not the smaller known count.
            DataSchemaVersion = Math.Max(dataCurrent, DataOwnedMigrations.Count);

            string? consumerApplied = await ApplyMigrationPhaseAsync(
                connection, "App", _consumerMigrations, consumerCurrent, Sql.Schema.InsertConsumerVersion);
            SchemaVersion = Math.Max(consumerCurrent, _consumerMigrations.Count);

            MigrationApplied = CombineMigrationApplied(dataApplied, consumerApplied);
            if (SchemaVersionOvershootDetected)
                Logger.LogSchemaVersionOvershoot(DataSchemaVersion, DataOwnedMigrations.Count, SchemaVersion, _consumerMigrations.Count);
        }
        catch (Exception ex) when (backupPath is not null)
        {
            Logger.LogError(ex, "[Database - Init] migration failed — restoring pre-migration backup, database left unchanged...");
            RestoreBackup(connection, backupPath);
            Logger.LogInformation("[Database - Init] pre-migration backup restored.");
            throw;
        }
        finally
        {
            await connection.ExecuteAsync("PRAGMA foreign_keys = ON;");
        }

        Logger.LogSchemaUpdated(DataSchemaVersion, SchemaVersion);

        return false;
    }

    private async Task ApplyBaselineAsync(SqliteConnection connection)
    {
        Logger.LogCreatingSchemaAtBaseline(DataOwnedMigrations.Count, _consumerMigrations.Count);

        await connection.ExecuteAsync("PRAGMA foreign_keys = OFF;");
        try
        {
            using SqliteTransaction tx = connection.BeginTransaction();
            await connection.ExecuteAsync(DataBaselineSql, transaction: tx);
            await connection.ExecuteAsync(
                Sql.Schema.InsertDataVersion,
                new { v = DataOwnedMigrations.Count, at = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat) },
                transaction: tx);
            await connection.ExecuteAsync(_consumerBaseline!.Sql, transaction: tx);
            await connection.ExecuteAsync(
                Sql.Schema.InsertConsumerVersion,
                new { v = _consumerMigrations.Count, at = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat) },
                transaction: tx);
            await tx.CommitAsync();
        }
        finally
        {
            await connection.ExecuteAsync("PRAGMA foreign_keys = ON;");
        }

        DataSchemaVersion = DataOwnedMigrations.Count;
        SchemaVersion     = _consumerMigrations.Count;
        Logger.LogSchemaCreatedAtBaseline(DataSchemaVersion, SchemaVersion);

        await SeedSystemContentAsync(connection);
    }

    /// <summary>
    /// Applies one migration phase (either Quotinator.Data's own list or the consumer's own list)
    /// against its own version table, starting from <paramref name="current"/>. Returns a
    /// human-readable <c>"{Phase} vX → vY"</c> description if any migration in this phase actually
    /// ran, or <c>null</c> if the phase was already up to date. No exception handling here — if a
    /// migration's SQL throws, <c>using var tx</c> rolls back on unwind and the exception propagates
    /// untouched to the caller, which is responsible for the broader roll-back-to-previous-state
    /// (see <see cref="ApplyMigrationsAsync"/> and <see cref="DropAndRebuildAsync"/>).
    /// </summary>
    private async Task<string?> ApplyMigrationPhaseAsync(
        SqliteConnection connection,
        string phaseName,
        IReadOnlyList<SchemaMigration> migrations,
        int current,
        string insertVersionSql)
    {
        if (current >= migrations.Count) return null;

        Logger.LogApplyingMigrationPhase(migrations.Count - current, phaseName, current, migrations.Count);

        for (int i = current; i < migrations.Count; i++)
        {
            using SqliteTransaction tx = connection.BeginTransaction();
            await connection.ExecuteAsync(migrations[i].Sql, transaction: tx);
            await connection.ExecuteAsync(
                insertVersionSql,
                new { v = i + 1, at = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat) },
                transaction: tx);
            await tx.CommitAsync();
        }

        return $"{phaseName} v{current} → v{migrations.Count}";
    }

    private static string? CombineMigrationApplied(string? dataApplied, string? consumerApplied)
        => (dataApplied, consumerApplied) switch
        {
            (null, null)     => null,
            (_, null)        => dataApplied,
            (null, _)        => consumerApplied,
            _                => $"{dataApplied}, {consumerApplied}"
        };

    // Discovers all user tables at runtime and drops them.
    // Table names come from sqlite_master (system metadata) — string interpolation is safe.
    private static async Task DropAllTablesAsync(SqliteConnection connection)
    {
        List<string> tables = [.. await connection.QueryAsync<string>(Sql.Schema.GetAllTables)];
        foreach (string table in tables)
            await connection.ExecuteAsync($"DROP TABLE IF EXISTS [{table}];");
    }

    #endregion
}

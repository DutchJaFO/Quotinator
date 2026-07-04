using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Quotinator.Data.Connections;
using Quotinator.Data.Import;
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
public class DatabaseInitializer : IDatabaseInitializer
{
    // Quotinator.Data's own migrations, for its own tables (System_AuditEntries currently; any
    // future System_-prefixed table Quotinator.Data itself defines). Never passed through the
    // constructor — Quotinator.Data owns and maintains these scripts itself, and they always
    // apply before any consumer-supplied migration, tracked in their own System_SchemaVersion
    // table, independent of the consumer's own System_ConsumerSchemaVersion count.
    private static readonly IReadOnlyList<SchemaMigration> DataOwnedMigrations =
    [
        new SchemaMigration { Version = 1, Sql = AuditMigrations.CreateAuditEntriesTable },
        new SchemaMigration { Version = 2, Sql = AuditMigrations.RenameAuditEntriesToSystemAuditEntries },
    ];

    // Data's own baseline fragment — creates System_AuditEntries directly under its final name for
    // a genuinely fresh database, skipping the historical create-then-rename dance entirely.
    private const string DataBaselineSql = """
        CREATE TABLE IF NOT EXISTS System_AuditEntries (
            Id          INTEGER PRIMARY KEY AUTOINCREMENT,
            TableName   TEXT    NOT NULL,
            RecordId    TEXT,
            Operation   TEXT    NOT NULL,
            Agent       TEXT,
            PerformedAt TEXT    NOT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_System_AuditEntries_TableName_RecordId ON System_AuditEntries (TableName, RecordId);
        CREATE INDEX IF NOT EXISTS IX_System_AuditEntries_PerformedAt ON System_AuditEntries (PerformedAt);
        """;

    private readonly IDbConnectionFactory           _factory;
    private readonly DatabaseOptions                _options;
    private readonly IReadOnlyList<SchemaMigration> _consumerMigrations;
    private readonly SchemaBaseline?                _consumerBaseline;

    /// <summary>Logger available to this class and subclasses.</summary>
    protected readonly ILogger Logger;

    /// <summary>Audit writer available to subclasses for recording reseed and reset operations.</summary>
    protected readonly ISystemAuditWriter AuditWriter;

    /// <summary>Caller context available to subclasses for populating audit entries.</summary>
    protected readonly ICallerContext CallerContext;

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
    public string? MigrationApplied { get; protected set; }

    /// <inheritdoc/>
    public IReadOnlyList<SeedDuplicateRecord> LastSeedDuplicates { get; protected set; } = [];

    // Guards against concurrent seeding when multiple WebApplicationFactory instances start in
    // the same process (e.g. parallel MSTest runs). Each waiter re-checks COUNT(*) after
    // acquiring the lock and skips seeding if the previous holder already populated the DB.
    private static readonly SemaphoreSlim SeedLock = new(1, 1);

    /// <summary>A semaphore that subclasses must acquire before performing seeding operations, to prevent concurrent seed runs.</summary>
    protected static SemaphoreSlim SharedSeedLock => SeedLock;

    /// <summary>Initialises the instance with connection factory, options, and ordered schema migrations.</summary>
    /// <param name="factory">Factory used to open SQLite connections.</param>
    /// <param name="options">Database file paths and settings.</param>
    /// <param name="migrations">Ordered, append-only list of the consuming project's own schema migrations to apply. Always applied after Quotinator.Data's own migrations.</param>
    /// <param name="auditWriter">Writes audit entries for reseed and reset operations.</param>
    /// <param name="callerContext">Provides the agent identifier for audit entries.</param>
    /// <param name="logger">Logger for startup diagnostics.</param>
    /// <param name="baseline">Optional consolidated DDL for the consuming project's own schema, used to create a genuinely fresh database in one step instead of replaying <paramref name="migrations"/>. When omitted, a fresh database always takes the full incremental path.</param>
    public DatabaseInitializer(
        IDbConnectionFactory           factory,
        DatabaseOptions                options,
        IReadOnlyList<SchemaMigration> migrations,
        ISystemAuditWriter             auditWriter,
        ICallerContext                 callerContext,
        ILogger<DatabaseInitializer>   logger,
        SchemaBaseline?                baseline = null)
    {
        _factory            = factory;
        _options            = options;
        _consumerMigrations = migrations;
        _consumerBaseline   = baseline;
        AuditWriter         = auditWriter;
        CallerContext       = callerContext;
        Logger              = logger;
    }

    /// <inheritdoc/>
    public async Task InitialiseAsync()
    {
        MigrateFilenameIfNeeded();

        using var connection = (SqliteConnection)_factory.CreateConnection();
        await connection.OpenAsync();

        EnableWal(connection);
        await ApplyMigrationsAsync(connection);
        await OnInitialisedAsync(connection);
    }

    /// <summary>
    /// Test-only entry point that mirrors <see cref="InitialiseAsync"/> but can force the
    /// incremental migration path even on an empty database, bypassing the baseline short-circuit.
    /// Used by schema-drift tests to produce a "pure incremental" comparison database.
    /// </summary>
    internal async Task InitialiseForTestingAsync(bool forceIncremental)
    {
        MigrateFilenameIfNeeded();

        using var connection = (SqliteConnection)_factory.CreateConnection();
        await connection.OpenAsync();

        EnableWal(connection);
        await ApplyMigrationsAsync(connection, forceIncremental);
        await OnInitialisedAsync(connection);
    }

    /// <summary>
    /// Called after migrations are applied. Override to perform domain-specific seeding and
    /// statistics collection. The base implementation is a no-op.
    /// </summary>
    protected virtual Task OnInitialisedAsync(SqliteConnection connection) => Task.CompletedTask;

    /// <inheritdoc/>
    public async Task ReseedAsync(bool forceSourceRefresh = false)
    {
        using var connection = (SqliteConnection)_factory.CreateConnection();
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
        using var connection = (SqliteConnection)_factory.CreateConnection();
        await connection.OpenAsync();
        await OnResetAsync(connection, preserveSchemaVersion, forceSourceRefresh);
    }

    /// <summary>
    /// Called by <see cref="ResetAsync"/>. Override to replace the default no-op with a
    /// domain-specific reset implementation. Base implementation does nothing.
    /// </summary>
    protected virtual Task OnResetAsync(SqliteConnection connection, bool preserveSchemaVersion, bool forceSourceRefresh) => Task.CompletedTask;

    /// <inheritdoc/>
    public virtual Task<SeedPreviewResult> PreviewSeedAsync()
        => Task.FromResult(new SeedPreviewResult([], [], 0, 0));

    /// <inheritdoc/>
    public virtual Task<SourceCacheResolution> RefreshSourcesAsync(bool force = false)
        => Task.FromResult(new SourceCacheResolution([], []));

    // -------------------------------------------------------------------------
    #region Protected utilities for subclasses

    /// <summary>Truncates all quote-related data tables. Subclasses call this during reseed/reset.</summary>
    protected static async Task TruncateDataAsync(SqliteConnection connection)
    {
        await connection.ExecuteAsync("PRAGMA foreign_keys = OFF;");
        await connection.ExecuteAsync(Sql.QuoteGenres.DeleteAll);
        await connection.ExecuteAsync(Sql.QuoteTranslations.DeleteAll);
        await connection.ExecuteAsync(Sql.SourceTranslations.DeleteAll);
        await connection.ExecuteAsync(Sql.CharacterTranslations.DeleteAll);
        await connection.ExecuteAsync(Sql.Quotes.DeleteAll);
        await connection.ExecuteAsync(Sql.Characters.DeleteAll);
        await connection.ExecuteAsync(Sql.People.DeleteAll);
        await connection.ExecuteAsync(Sql.Sources.DeleteAll);
        await connection.ExecuteAsync(Sql.ImportBatches.DeleteAll);
        await connection.ExecuteAsync("PRAGMA foreign_keys = ON;");
    }

    /// <summary>
    /// Drops and recreates the consumer's own domain tables by reapplying its migrations.
    /// Subclasses call this during reset. <c>System_AuditEntries</c> is never dropped (see
    /// <see cref="Sql.Schema.GetUserTables"/>), and — because Quotinator.Data's own migrations
    /// concern only <c>System_</c>-prefixed tables that a Reset never touches — Quotinator.Data's
    /// own migration history (<c>System_SchemaVersion</c>) is never wiped or replayed here either,
    /// regardless of <paramref name="preserveSchemaVersion"/>. Only <c>System_ConsumerSchemaVersion</c>
    /// is cleared and replayed; when <paramref name="preserveSchemaVersion"/> is <c>true</c>, its
    /// rows are snapshotted first and restored afterward. A full backup is always taken before any
    /// destructive step; any failure anywhere in the rebuild restores it and rethrows, without
    /// attempting to interpret what went wrong.
    /// </summary>
    protected async Task DropAndRebuildAsync(SqliteConnection connection, bool preserveSchemaVersion = false)
    {
        var savedConsumerVersions = preserveSchemaVersion
            ? (await connection.QueryAsync<SystemSchemaVersionRow>(Sql.Schema.GetAllConsumerVersions)).ToList()
            : [];

        var backupPath = CreateBackup(connection, SchemaVersion);

        try
        {
            await connection.ExecuteAsync("PRAGMA foreign_keys = OFF;");
            await connection.ExecuteAsync(Sql.Schema.DeleteAllConsumerVersions);
            await DropAllTablesAsync(connection);
            await connection.ExecuteAsync("PRAGMA foreign_keys = ON;");
            await ApplyMigrationsAsync(connection, skipOwnBackup: true);

            if (!preserveSchemaVersion) return;

            await connection.ExecuteAsync(Sql.Schema.DeleteAllConsumerVersions);
            foreach (var row in savedConsumerVersions)
                await connection.ExecuteAsync(Sql.Schema.InsertConsumerVersion, new { v = row.Version, at = row.AppliedAt });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[Database - Init] reset failed — restoring pre-reset backup, database left unchanged...");
            RestoreBackup(connection, backupPath);
            Logger.LogInformation("[Database - Init] pre-reset backup restored.");
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
        var dataDir    = Path.GetDirectoryName(_options.DbPath)!;
        var legacyPath = Path.Combine(dataDir, DataPaths.LegacyDatabaseFile);
        if (!File.Exists(legacyPath) || File.Exists(_options.DbPath)) return;

        Logger.LogInformation("[Database - Init] migrating legacy filename quotes.db → {NewName}", Path.GetFileName(_options.DbPath));
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var src = legacyPath + suffix;
            var dst = _options.DbPath + suffix;
            if (!File.Exists(src)) continue;
            Logger.LogInformation("[Database - Init] moving {Src} → {Dst}", Path.GetFileName(src), Path.GetFileName(dst));
            File.Move(src, dst);
        }
        Logger.LogInformation("[Database - Init] filename migration complete → {Path}", _options.DbPath);
    }

    private string CreateBackup(SqliteConnection connection, int fromVersion)
    {
        Directory.CreateDirectory(_options.BackupsPath);
        var timestamp  = DateTime.UtcNow.ToString("yyyyMMddTHHmmss");
        var backupName = $"{Path.GetFileNameWithoutExtension(_options.DbPath)}_v{fromVersion}_{timestamp}Z.db";
        var backupPath = Path.Combine(_options.BackupsPath, backupName);

        Logger.LogInformation("[Database - Backup] backing up v{Version} → {Path}", fromVersion, backupPath);
        using var dest = new SqliteConnection($"Data Source={backupPath}");
        dest.Open();
        connection.BackupDatabase(dest);
        Logger.LogInformation("[Database - Backup] backup complete");
        return backupPath;
    }

    // Restores a backup file created by CreateBackup back into the live connection — the reverse
    // direction of the same SQLite online-backup API. Used when a migration attempt fails partway
    // through, so the caller is left with the database exactly as it was before the attempt started
    // rather than a partially-migrated or partially-rebuilt one.
    private static void RestoreBackup(SqliteConnection connection, string backupPath)
    {
        using var source = new SqliteConnection($"Data Source={backupPath}");
        source.Open();
        source.BackupDatabase(connection);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Migrations

    private static void EnableWal(SqliteConnection connection)
        => connection.Execute("PRAGMA journal_mode=WAL;");

    // One-time bootstrap step, run before either version table's own CREATE TABLE IF NOT EXISTS and
    // before either current migration version is known — SchemaVersion predates the numbered
    // migration list entirely, so its rename can't be a numbered migration. A fresh database has no
    // table literally named SchemaVersion, so this is a no-op — a new database is never created
    // under the old name and then renamed. Only concerns Data's own table; System_ConsumerSchemaVersion
    // is a brand-new table with no legacy name to migrate from.
    private static async Task RenameLegacySchemaVersionTableIfPresentAsync(SqliteConnection connection)
    {
        var legacyExists = await connection.ExecuteScalarAsync<int>(Sql.Schema.LegacySchemaVersionExists);
        if (legacyExists == 0) return;

        await connection.ExecuteAsync(Sql.Schema.RenameLegacySchemaVersionTable);
    }

    private async Task ApplyMigrationsAsync(SqliteConnection connection, bool forceIncremental = false, bool skipOwnBackup = false)
    {
        await RenameLegacySchemaVersionTableIfPresentAsync(connection);

        // Must run before either CreateXVersionTable call below — those would otherwise make every
        // fresh database register as "not empty" on the very next line, permanently disabling the
        // baseline path.
        var isEmptyDatabase = await connection.ExecuteScalarAsync<int>(Sql.Schema.AnyTableExists) == 0;

        await connection.ExecuteAsync(Sql.Schema.CreateDataVersionTable);
        await connection.ExecuteAsync(Sql.Schema.CreateConsumerVersionTable);

        if (isEmptyDatabase && !forceIncremental && _consumerBaseline is not null)
        {
            await ApplyBaselineAsync(connection);
            return;
        }

        var dataCurrent     = await connection.ExecuteScalarAsync<int>(Sql.Schema.GetDataCurrentVersion);
        var consumerCurrent = await connection.ExecuteScalarAsync<int>(Sql.Schema.GetConsumerCurrentVersion);

        var dataPending     = dataCurrent     < DataOwnedMigrations.Count;
        var consumerPending = consumerCurrent < _consumerMigrations.Count;

        if (!dataPending && !consumerPending)
        {
            DataSchemaVersion = dataCurrent;
            SchemaVersion     = consumerCurrent;
            Logger.LogInformation(
                "[Database - Init] schema is up to date (data v{DataVersion}, app v{AppVersion})",
                dataCurrent, consumerCurrent);
            return;
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
            var dataApplied = await ApplyMigrationPhaseAsync(
                connection, "Data", DataOwnedMigrations, dataCurrent, Sql.Schema.InsertDataVersion);
            DataSchemaVersion = DataOwnedMigrations.Count;

            var consumerApplied = await ApplyMigrationPhaseAsync(
                connection, "App", _consumerMigrations, consumerCurrent, Sql.Schema.InsertConsumerVersion);
            SchemaVersion = _consumerMigrations.Count;

            MigrationApplied = CombineMigrationApplied(dataApplied, consumerApplied);
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

        Logger.LogInformation(
            "[Database - Init] schema updated (data v{DataVersion}, app v{AppVersion})",
            DataSchemaVersion, SchemaVersion);
    }

    private async Task ApplyBaselineAsync(SqliteConnection connection)
    {
        Logger.LogInformation(
            "[Database - Init] fresh database detected — creating schema directly at baseline " +
            "(data v{DataVersion}, app v{AppVersion})...",
            DataOwnedMigrations.Count, _consumerMigrations.Count);

        await connection.ExecuteAsync("PRAGMA foreign_keys = OFF;");
        try
        {
            using var tx = connection.BeginTransaction();
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
        Logger.LogInformation(
            "[Database - Init] schema created at baseline (data v{DataVersion}, app v{AppVersion})",
            DataSchemaVersion, SchemaVersion);
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

        Logger.LogInformation(
            "[Database - Init] applying {Count} pending {Phase} migration(s) (version {Current} → {Target})...",
            migrations.Count - current, phaseName, current, migrations.Count);

        for (var i = current; i < migrations.Count; i++)
        {
            using var tx = connection.BeginTransaction();
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
        var tables = (await connection.QueryAsync<string>(Sql.Schema.GetUserTables)).ToList();
        foreach (var table in tables)
            await connection.ExecuteAsync($"DROP TABLE IF EXISTS [{table}];");
    }

    #endregion
}

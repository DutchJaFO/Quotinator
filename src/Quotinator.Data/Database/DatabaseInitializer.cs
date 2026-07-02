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
    private readonly IDbConnectionFactory           _factory;
    private readonly DatabaseOptions                _options;
    private readonly IReadOnlyList<SchemaMigration> _migrations;

    /// <summary>Logger available to this class and subclasses.</summary>
    protected readonly ILogger Logger;

    /// <summary>Audit writer available to subclasses for recording reseed and reset operations.</summary>
    protected readonly ISystemAuditWriter AuditWriter;

    /// <summary>Caller context available to subclasses for populating audit entries.</summary>
    protected readonly ICallerContext CallerContext;

    /// <inheritdoc/>
    public int SchemaVersion { get; protected set; }

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
    /// <param name="migrations">Ordered, append-only list of schema migrations to apply.</param>
    /// <param name="auditWriter">Writes audit entries for reseed and reset operations.</param>
    /// <param name="callerContext">Provides the agent identifier for audit entries.</param>
    /// <param name="logger">Logger for startup diagnostics.</param>
    public DatabaseInitializer(
        IDbConnectionFactory           factory,
        DatabaseOptions                options,
        IReadOnlyList<SchemaMigration> migrations,
        ISystemAuditWriter             auditWriter,
        ICallerContext                 callerContext,
        ILogger<DatabaseInitializer>   logger)
    {
        _factory      = factory;
        _options      = options;
        _migrations   = migrations;
        AuditWriter   = auditWriter;
        CallerContext = callerContext;
        Logger        = logger;
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
    /// Called after migrations are applied. Override to perform domain-specific seeding and
    /// statistics collection. The base implementation is a no-op.
    /// </summary>
    protected virtual Task OnInitialisedAsync(SqliteConnection connection) => Task.CompletedTask;

    /// <inheritdoc/>
    public async Task ReseedAsync()
    {
        using var connection = (SqliteConnection)_factory.CreateConnection();
        await connection.OpenAsync();
        await OnReseedAsync(connection);
    }

    /// <summary>
    /// Called by <see cref="ReseedAsync"/>. Override to replace the default no-op with a
    /// domain-specific reseed implementation. Base implementation does nothing.
    /// </summary>
    protected virtual Task OnReseedAsync(SqliteConnection connection) => Task.CompletedTask;

    /// <inheritdoc/>
    public async Task ResetAsync(bool preserveSchemaVersion = false)
    {
        using var connection = (SqliteConnection)_factory.CreateConnection();
        await connection.OpenAsync();
        await OnResetAsync(connection, preserveSchemaVersion);
    }

    /// <summary>
    /// Called by <see cref="ResetAsync"/>. Override to replace the default no-op with a
    /// domain-specific reset implementation. Base implementation does nothing.
    /// </summary>
    protected virtual Task OnResetAsync(SqliteConnection connection, bool preserveSchemaVersion) => Task.CompletedTask;

    /// <inheritdoc/>
    public virtual Task<SeedPreviewResult> PreviewSeedAsync()
        => Task.FromResult(new SeedPreviewResult([], [], 0, 0));

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
    /// Drops and recreates all tables by reapplying migrations. Subclasses call this during reset.
    /// <c>System_AuditEntries</c> is never dropped (see <see cref="Sql.Schema.GetUserTables"/>). The
    /// rebuild always clears and replays <c>System_SchemaVersion</c> exactly as before — this is
    /// what makes every data table come back — but when <paramref name="preserveSchemaVersion"/> is
    /// <c>true</c>, the rows that existed before the rebuild are snapshotted first and restored
    /// afterward, so the caller observes no change to schema version history even though the
    /// rebuild ran underneath.
    /// </summary>
    protected async Task DropAndRebuildAsync(SqliteConnection connection, bool preserveSchemaVersion = false)
    {
        var savedVersions = preserveSchemaVersion
            ? (await connection.QueryAsync<SystemSchemaVersionRow>(Sql.Schema.GetAllVersions)).ToList()
            : [];

        await connection.ExecuteAsync("PRAGMA foreign_keys = OFF;");
        await connection.ExecuteAsync(Sql.Schema.DeleteAll);
        await DropAllTablesAsync(connection);
        await connection.ExecuteAsync("PRAGMA foreign_keys = ON;");
        await ApplyMigrationsAsync(connection);

        if (!preserveSchemaVersion) return;

        await connection.ExecuteAsync(Sql.Schema.DeleteAll);
        foreach (var row in savedVersions)
            await connection.ExecuteAsync(Sql.Schema.InsertVersion, new { v = row.Version, at = row.AppliedAt });
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

    private void CreateBackup(SqliteConnection connection, int fromVersion)
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
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Migrations

    private static void EnableWal(SqliteConnection connection)
        => connection.Execute("PRAGMA journal_mode=WAL;");

    // One-time bootstrap step, run before System_SchemaVersion's own CREATE TABLE IF NOT EXISTS and
    // before the current migration version is known — SchemaVersion predates the numbered migration
    // list entirely, so its rename can't be a numbered migration. A fresh database has no table
    // literally named SchemaVersion, so this is a no-op and CreateTable proceeds straight to the
    // final table name — a new database is never created under the old name and then renamed.
    private static async Task RenameLegacySchemaVersionTableIfPresentAsync(SqliteConnection connection)
    {
        var legacyExists = await connection.ExecuteScalarAsync<int>(Sql.Schema.LegacySchemaVersionExists);
        if (legacyExists == 0) return;

        await connection.ExecuteAsync(Sql.Schema.RenameLegacySchemaVersionTable);
    }

    private async Task ApplyMigrationsAsync(SqliteConnection connection)
    {
        await RenameLegacySchemaVersionTableIfPresentAsync(connection);
        await connection.ExecuteAsync(Sql.Schema.CreateTable);

        var current = await connection.ExecuteScalarAsync<int>(Sql.Schema.GetCurrentVersion);

        if (current >= _migrations.Count)
        {
            SchemaVersion = current;
            Logger.LogInformation("[Database - Init] schema is up to date at version {Version}", current);
            return;
        }

        if (current == 0)
        {
            Logger.LogInformation("[Database - Init] creating schema...");
        }
        else
        {
            Logger.LogInformation(
                "[Database - Init] applying {Count} pending migration(s) (version {Current} → {Target})...",
                _migrations.Count - current, current, _migrations.Count);
            CreateBackup(connection, current);
        }

        // Some migrations recreate a table (SQLite has no ALTER ... CHECK) to widen a constraint,
        // which requires dropping a table that other tables still hold live foreign-key references
        // to. Foreign key enforcement must be off for the duration — PRAGMA foreign_keys is a no-op
        // inside a transaction, so it cannot be toggled from within a migration's own SQL text.
        await connection.ExecuteAsync("PRAGMA foreign_keys = OFF;");
        try
        {
            await ApplyPendingMigrationsAsync(connection, current);
        }
        finally
        {
            await connection.ExecuteAsync("PRAGMA foreign_keys = ON;");
        }
    }

    private async Task ApplyPendingMigrationsAsync(SqliteConnection connection, int current)
    {
        for (var i = current; i < _migrations.Count; i++)
        {
            using var tx = connection.BeginTransaction();
            try
            {
                await connection.ExecuteAsync(_migrations[i].Sql, transaction: tx);
                await connection.ExecuteAsync(
                    Sql.Schema.InsertVersion,
                    new { v = i + 1, at = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat) },
                    transaction: tx);
                await tx.CommitAsync();
            }
            catch (SqliteException ex) when (IsKnownMigrationError(ex, i + 1))
            {
                await tx.RollbackAsync();

                var isSystemAuditEntriesCollision =
                    i + 1 == 6 && ex.Message.Contains("already another table", StringComparison.OrdinalIgnoreCase);

                if (isSystemAuditEntriesCollision)
                {
                    // Expected on every default Reset, not a sign of a broken database: System_AuditEntries
                    // survives the table wipe by design (Sql.Schema.GetUserTables), so replaying migration004
                    // recreates a stray empty AuditEntries that collides with migration006's rename target.
                    Logger.LogInformation(
                        "[Database - Init] migration 6 rename target already exists — System_AuditEntries is " +
                        "protected from Reset by design. Recording version and continuing.");
                    await connection.ExecuteAsync(Sql.SystemAudit.DropStrayLegacyAuditEntriesTable);
                }
                else
                {
                    Logger.LogWarning(
                        "[Database - Init] migration {Version} was previously partially applied — " +
                        "recording version and continuing. If data appears missing, use Reset Database.",
                        i + 1);
                }

                await connection.ExecuteAsync(
                    Sql.Schema.InsertVersion,
                    new { v = i + 1, at = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat) });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                Logger.LogError(
                    ex,
                    "[Database - Init] migration {Version} failed — rolled back to version {Current}. " +
                    "Resolve the issue and restart the application.",
                    i + 1, i);
                throw;
            }
        }

        SchemaVersion = _migrations.Count;
        if (current > 0)
            MigrationApplied = $"v{current} → v{_migrations.Count}";
        Logger.LogInformation(
            "[Database - Init] schema {Action} at version {Version}",
            current == 0 ? "created" : "updated", _migrations.Count);
    }

    // Returns true only for explicitly documented per-version known errors.
    private static bool IsKnownMigrationError(SqliteException ex, int migrationVersion)
        => migrationVersion switch
        {
            // Migration003 added ImportBatchId columns via ALTER TABLE ADD COLUMN. The broken
            // ResetAsync in v1.5.x–v1.6.1 could apply this migration's DDL without recording
            // the version, leaving the columns present but SchemaVersion stuck at 2. On the
            // next startup the ALTER TABLE fails because the column already exists.
            3 => ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase),
            // Migration006 renames AuditEntries to System_AuditEntries via ALTER TABLE ... RENAME
            // TO, which has no IF EXISTS guard in SQLite. Two known-recoverable cases:
            // (a) a prior partial application already completed the rename but failed before
            //     recording the version — retrying the ALTER TABLE fails because AuditEntries no
            //     longer exists.
            // (b) System_AuditEntries is protected from the Reset table wipe (Sql.Schema.GetUserTables),
            //     so on a full migration replay migration004's CREATE TABLE IF NOT EXISTS recreates
            //     a stray empty AuditEntries, and the rename then fails because the destination —
            //     the real, preserved System_AuditEntries — already exists.
            6 => ex.Message.Contains("no such table: AuditEntries", StringComparison.OrdinalIgnoreCase)
                 || ex.Message.Contains("already another table or index with this name: System_AuditEntries", StringComparison.OrdinalIgnoreCase),
            _ => false
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

using System.Data.Common;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quotinator.Data.Connections;
using Quotinator.Data.Enums;
using Quotinator.Data.Logging;
using Quotinator.Data.Queries;
using Quotinator.Data.Schema;

namespace Quotinator.Data.Database;

/// <summary>
/// Creates and migrates the separate changelog database's schema (#309, ADR 018). Deliberately a
/// small, independent class rather than a second instance of <see cref="DatabaseInitializer"/> —
/// that class hardcodes the main database's own migration list
/// (<c>Import_Batch</c>/<c>Audit_Entry</c>/<c>System_Notification</c>/etc.) as a private field, not a
/// constructor parameter, so instantiating it a second time would try to create those tables in the
/// changelog database too. This class follows the same baseline-vs-incremental pattern (empty database
/// → one-step baseline; otherwise replay pending migrations), per ADR 018's correction: every database
/// gets real migration capability, even one whose default (in-memory) storage mode always starts empty
/// and so always takes the baseline path in practice today.
/// </summary>
/// <remarks>Initialises the instance with the changelog database's own keyed connection factory and logger.</remarks>
/// <param name="factory">The keyed <see cref="IDbConnectionFactory"/> for the changelog database (see <see cref="DatabaseConnectionKeys.Changelog"/>).</param>
/// <param name="logger">Logger for startup diagnostics.</param>
public sealed class ChangelogDatabaseInitializer(
    [FromKeyedServices(DatabaseConnectionKeys.Changelog)] IDbConnectionFactory factory,
    ILogger<ChangelogDatabaseInitializer> logger) : IIntegrityCheckedDatabase
{
    // Append-only, same discipline as DatabaseInitializer.DataOwnedMigrations — once applied to any
    // real (persistent-file-variant) database, a migration here is frozen too.
    private static readonly IReadOnlyList<SchemaMigration> Migrations =
    [
        new SchemaMigration
        {
            Version     = 1,
            Sql         = ChangelogContentMigrations.CreateChangelogTables,
            Description = "Creates the tables the changelog is served from.",
        },
    ];

    // Identical content to Migrations[0].Sql today (there is only one migration yet) — kept as its
    // own named constant, not a reference to Migrations[0], so the two are free to diverge the moment
    // a second migration exists, matching DatabaseInitializer's own baseline/incremental separation.
    private const string BaselineSql = ChangelogContentMigrations.CreateChangelogTables;

    /// <summary>The current schema version, available after <see cref="InitialiseAsync()"/> completes.</summary>
    public int SchemaVersion { get; private set; }

    /// <inheritdoc/>
    public string DatabaseName => "changelog";

    /// <inheritdoc/>
    public SchemaIntegrityResult? LastIntegrityResult { get; private set; }

    /// <inheritdoc/>
    public async Task<SchemaIntegrityResult> CheckIntegrityAsync()
    {
        SchemaSnapshot expected = await SnapshotReader.ReadScriptsAsync(ExpectedSchemaScripts());

        using DbConnection connection = (DbConnection)factory.CreateConnection();
        await connection.OpenAsync();
        SchemaSnapshot actual = await SnapshotReader.ReadAsync(connection);

        return SchemaIntegrityComparer.Compare(expected, actual);
    }

    /// <summary>
    /// The DDL that defines the changelog database's schema (#305): exactly what a fresh changelog database
    /// runs. Static so a tool can derive the same expected schema without a configured initializer.
    /// </summary>
    public static IReadOnlyList<string> ExpectedSchemaScripts() => [Sql.ChangelogSchema.CreateVersionTable, BaselineSql];

    // Stateless, and needed by an instance and a static caller alike, so it is not injected: there is
    // exactly one implementation (ADR 003), and this is the only place in the check that names it.
    private static readonly ISchemaSnapshotReader SnapshotReader = SqliteSchemaSnapshotReader.Instance;

    /// <summary>Applies the baseline (genuinely empty database) or any pending migrations.</summary>
    public Task InitialiseAsync() => InitialiseAsync(forceIncremental: false);

    /// <summary>
    /// Test-only entry point that forces the incremental path even on an empty database, bypassing
    /// the baseline short-circuit — used by the schema-drift parity test to produce a "pure
    /// incremental" comparison database, mirroring <see cref="DatabaseInitializer.InitialiseForTestingAsync"/>.
    /// </summary>
    internal Task InitialiseForTestingAsync(bool forceIncremental) => InitialiseAsync(forceIncremental);

    private async Task InitialiseAsync(bool forceIncremental)
    {
        await ApplySchemaAsync(forceIncremental);
        await RecordIntegrityAsync();
    }

    /// <summary>
    /// Runs the integrity check after this database's migrations (#305), records it as
    /// <see cref="LastIntegrityResult"/>, and logs every difference and every extra object. A result that
    /// is not intact does not fail initialisation: the caller falls back to the JSON files (R2).
    /// </summary>
    private async Task RecordIntegrityAsync()
    {
        SchemaIntegrityResult result = await CheckIntegrityAsync();
        LastIntegrityResult = result;

        foreach (SchemaDifference difference in result.Differences)
        {
            if (difference.Kind == SchemaDifferenceKind.Extra)
                logger.LogChangelogSchemaExtraObject(difference.ObjectKind.ToString(), difference.ObjectName);
            else
                logger.LogChangelogSchemaDifference(
                    difference.Kind.ToString(), difference.ObjectKind.ToString(), difference.ObjectName,
                    difference.Aspect is null ? string.Empty : $" ({difference.Aspect}: expected '{difference.Expected}', actual '{difference.Actual}')");
        }

        if (!result.IsIntact)
            logger.LogChangelogSchemaNotIntact(result.Differences.Count(d => d.Kind != SchemaDifferenceKind.Extra));
    }

    private async Task ApplySchemaAsync(bool forceIncremental)
    {
        using SqliteConnection connection = (SqliteConnection)factory.CreateConnection();
        await connection.OpenAsync();

        // Must run before Sql.ChangelogSchema.CreateVersionTable below — otherwise every fresh
        // database registers as "not empty" on the very next line, permanently disabling the
        // baseline path. Matches DatabaseInitializer.ApplyMigrationsAsync's own ordering for the
        // identical reason.
        bool isEmptyDatabase = await connection.ExecuteScalarAsync<int>(Sql.ChangelogSchema.AnyTableExists) == 0;

        await connection.ExecuteAsync(Sql.ChangelogSchema.CreateVersionTable);

        if (isEmptyDatabase && !forceIncremental)
        {
            await ApplyBaselineAsync(connection);
            return;
        }

        int current = await connection.ExecuteScalarAsync<int>(Sql.ChangelogSchema.GetCurrentVersion);

        if (current >= Migrations.Count)
        {
            SchemaVersion = current;
            logger.LogChangelogSchemaUpToDate(current);
            return;
        }

        for (int i = current; i < Migrations.Count; i++)
        {
            using SqliteTransaction tx = connection.BeginTransaction();
            await connection.ExecuteAsync(Migrations[i].Sql, transaction: tx);
            await connection.ExecuteAsync(
                Sql.ChangelogSchema.InsertVersion,
                new { v = i + 1, at = DateTime.UtcNow.ToString("O") },
                transaction: tx);
            await tx.CommitAsync();
        }

        SchemaVersion = Migrations.Count;
        logger.LogChangelogSchemaUpdated(current, Migrations.Count);
    }

    private async Task ApplyBaselineAsync(SqliteConnection connection)
    {
        using SqliteTransaction tx = connection.BeginTransaction();
        await connection.ExecuteAsync(BaselineSql, transaction: tx);
        await connection.ExecuteAsync(
            Sql.ChangelogSchema.InsertVersion,
            new { v = Migrations.Count, at = DateTime.UtcNow.ToString("O") },
            transaction: tx);
        await tx.CommitAsync();

        SchemaVersion = Migrations.Count;
        logger.LogChangelogSchemaCreatedAtBaseline(SchemaVersion);
    }
}

using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quotinator.Data.Connections;
using Quotinator.Data.Logging;
using Quotinator.Data.Queries;

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
    ILogger<ChangelogDatabaseInitializer> logger)
{
    // Append-only, same discipline as DatabaseInitializer.DataOwnedMigrations — once applied to any
    // real (persistent-file-variant) database, a migration here is frozen too.
    private static readonly IReadOnlyList<SchemaMigration> Migrations =
    [
        new SchemaMigration { Version = 1, Sql = ChangelogContentMigrations.CreateChangelogTables },
    ];

    // Identical content to Migrations[0].Sql today (there is only one migration yet) — kept as its
    // own named constant, not a reference to Migrations[0], so the two are free to diverge the moment
    // a second migration exists, matching DatabaseInitializer's own baseline/incremental separation.
    private const string BaselineSql = ChangelogContentMigrations.CreateChangelogTables;

    /// <summary>The current schema version, available after <see cref="InitialiseAsync()"/> completes.</summary>
    public int SchemaVersion { get; private set; }

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
        using var connection = (SqliteConnection)factory.CreateConnection();
        await connection.OpenAsync();

        // Must run before Sql.ChangelogSchema.CreateVersionTable below — otherwise every fresh
        // database registers as "not empty" on the very next line, permanently disabling the
        // baseline path. Matches DatabaseInitializer.ApplyMigrationsAsync's own ordering for the
        // identical reason.
        var isEmptyDatabase = await connection.ExecuteScalarAsync<int>(Sql.ChangelogSchema.AnyTableExists) == 0;

        await connection.ExecuteAsync(Sql.ChangelogSchema.CreateVersionTable);

        if (isEmptyDatabase && !forceIncremental)
        {
            await ApplyBaselineAsync(connection);
            return;
        }

        var current = await connection.ExecuteScalarAsync<int>(Sql.ChangelogSchema.GetCurrentVersion);

        if (current >= Migrations.Count)
        {
            SchemaVersion = current;
            logger.LogChangelogSchemaUpToDate(current);
            return;
        }

        for (var i = current; i < Migrations.Count; i++)
        {
            using var tx = connection.BeginTransaction();
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
        using var tx = connection.BeginTransaction();
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

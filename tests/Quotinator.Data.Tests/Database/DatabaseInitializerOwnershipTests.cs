using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.Database;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Data.Tests.Database;

/// <summary>
/// Proves the #143 ownership split at the base <see cref="DatabaseInitializer"/> level, with zero
/// consumer migrations/baseline involved — isolates Quotinator.Data's own behaviour from whatever
/// a consuming project (e.g. Quotinator.Engine) supplies.
/// </summary>
[TestClass]
public class DatabaseInitializerOwnershipTests
{
    private static DatabaseInitializer CreateBareInitializer(
        string dbPath, IReadOnlyList<SchemaMigration> consumerMigrations, SchemaBaseline? baseline = null)
    {
        var factory = new SqliteConnectionFactory(dbPath);
        var options = new DatabaseOptions
        {
            DbPath      = dbPath,
            BackupsPath = Path.Combine(Path.GetDirectoryName(dbPath)!, "backups"),
        };
        return new DatabaseInitializer(factory, options, consumerMigrations,
            NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance,
            NullLogger<DatabaseInitializer>.Instance, baseline);
    }

    private static async Task<List<string>> DumpTableSchemaAsync(SqliteConnection conn, string table)
    {
        var lines = new List<string>();

        var columns = await conn.QueryAsync<(int cid, string name, string type, int notnull, string? dflt_value, int pk)>(
            $"SELECT cid, name, type, [notnull], dflt_value, pk FROM pragma_table_info('{table}');");
        foreach (var c in columns.OrderBy(c => c.cid))
            lines.Add($"COL {c.cid} {c.name} {c.type} notnull={c.notnull} default={c.dflt_value} pk={c.pk}");

        var indexes = await conn.QueryAsync<(string name, int unique)>(
            $"SELECT name, [unique] FROM pragma_index_list('{table}');");
        foreach (var idx in indexes.OrderBy(i => i.name))
        {
            var idxCols = await conn.QueryAsync<(int seqno, string? name)>(
                $"SELECT seqno, name FROM pragma_index_info('{idx.name}');");
            var colList = string.Join(",", idxCols.OrderBy(c => c.seqno).Select(c => c.name));
            lines.Add($"IDX {idx.name} unique={idx.unique} cols=({colList})");
        }

        return lines;
    }

    // ── Data-side schema-drift proof ─────────────────────────────────────────

    /// <summary>
    /// Quotinator.Data's own baseline fragment (<c>DataBaselineSql</c>) must produce the exact same
    /// <c>System_AuditEntries</c> schema as replaying Quotinator.Data's own numbered migrations
    /// (<c>DataOwnedMigrations</c>) incrementally. This is what actually enforces "Data's own
    /// scripts stay in sync with each other," independent of whatever consumer exists — exercised
    /// here with zero consumer migrations and a no-op consumer baseline.
    /// </summary>
    [TestMethod]
    public async Task DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemAuditEntriesSchema()
    {
        using var tempA = new TempDatabase([]);
        var dbA = CreateBareInitializer(tempA.DbPath, [], baseline: new SchemaBaseline { Sql = "SELECT 1;" });
        await dbA.InitialiseAsync();

        using var tempB = new TempDatabase([]);
        var dbB = CreateBareInitializer(tempB.DbPath, []);
        await dbB.InitialiseForTestingAsync(forceIncremental: true);

        using var connA = new SqliteConnection($"Data Source={tempA.DbPath}");
        await connA.OpenAsync();
        using var connB = new SqliteConnection($"Data Source={tempB.DbPath}");
        await connB.OpenAsync();

        var schemaA = await DumpTableSchemaAsync(connA, "System_AuditEntries");
        var schemaB = await DumpTableSchemaAsync(connB, "System_AuditEntries");

        CollectionAssert.AreEqual(schemaB, schemaA,
            "System_AuditEntries schema differs between Data's baseline and incremental paths — " +
            "update DataBaselineSql to match DataOwnedMigrations' final result.");
    }

    /// <summary>
    /// Same proof as <see cref="DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemAuditEntriesSchema"/>,
    /// for <c>System_ImportConflicts</c> (added by #64's Data-owned migration 3, retrofitted onto
    /// <c>RecordBase</c> by migration 6, and given <c>ExistingBatchId</c> by migration 7 for #149).
    /// </summary>
    [TestMethod]
    public async Task DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemImportConflictsSchema()
    {
        using var tempA = new TempDatabase([]);
        var dbA = CreateBareInitializer(tempA.DbPath, [], baseline: new SchemaBaseline { Sql = "SELECT 1;" });
        await dbA.InitialiseAsync();

        using var tempB = new TempDatabase([]);
        var dbB = CreateBareInitializer(tempB.DbPath, []);
        await dbB.InitialiseForTestingAsync(forceIncremental: true);

        using var connA = new SqliteConnection($"Data Source={tempA.DbPath}");
        await connA.OpenAsync();
        using var connB = new SqliteConnection($"Data Source={tempB.DbPath}");
        await connB.OpenAsync();

        var schemaA = await DumpTableSchemaAsync(connA, "System_ImportConflicts");
        var schemaB = await DumpTableSchemaAsync(connB, "System_ImportConflicts");

        CollectionAssert.AreEqual(schemaB, schemaA,
            "System_ImportConflicts schema differs between Data's baseline and incremental paths — " +
            "update DataBaselineSql to match DataOwnedMigrations' final result.");
    }

    /// <summary>
    /// Same proof as <see cref="DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemAuditEntriesSchema"/>,
    /// for <c>System_ChangeLog</c> (added by #56's Data-owned migration 4).
    /// </summary>
    [TestMethod]
    public async Task DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemChangeLogSchema()
    {
        using var tempA = new TempDatabase([]);
        var dbA = CreateBareInitializer(tempA.DbPath, [], baseline: new SchemaBaseline { Sql = "SELECT 1;" });
        await dbA.InitialiseAsync();

        using var tempB = new TempDatabase([]);
        var dbB = CreateBareInitializer(tempB.DbPath, []);
        await dbB.InitialiseForTestingAsync(forceIncremental: true);

        using var connA = new SqliteConnection($"Data Source={tempA.DbPath}");
        await connA.OpenAsync();
        using var connB = new SqliteConnection($"Data Source={tempB.DbPath}");
        await connB.OpenAsync();

        var schemaA = await DumpTableSchemaAsync(connA, "System_ChangeLog");
        var schemaB = await DumpTableSchemaAsync(connB, "System_ChangeLog");

        CollectionAssert.AreEqual(schemaB, schemaA,
            "System_ChangeLog schema differs between Data's baseline and incremental paths — " +
            "update DataBaselineSql to match DataOwnedMigrations' final result.");
    }

    /// <summary>
    /// PRAGMA table_info/index_list do not capture CHECK constraint text, so a baseline that silently
    /// dropped a value from <c>InitiatedByType</c>'s or <c>Action</c>'s constraint (or introduced a
    /// typo) would pass the structural schema comparison above undetected. This behavioural round-trip
    /// closes that gap, for both the baseline and incremental paths.
    /// </summary>
    [TestMethod]
    public async Task DataOwnedBaseline_And_IncrementalReplay_AcceptSameChangeLogCheckConstraintValues()
    {
        using var tempA = new TempDatabase([]);
        var dbA = CreateBareInitializer(tempA.DbPath, [], baseline: new SchemaBaseline { Sql = "SELECT 1;" });
        await dbA.InitialiseAsync();

        using var tempB = new TempDatabase([]);
        var dbB = CreateBareInitializer(tempB.DbPath, []);
        await dbB.InitialiseForTestingAsync(forceIncremental: true);

        using var connA = new SqliteConnection($"Data Source={tempA.DbPath}");
        await connA.OpenAsync();
        using var connB = new SqliteConnection($"Data Source={tempB.DbPath}");
        await connB.OpenAsync();

        foreach (var conn in new[] { connA, connB })
        {
            var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            await conn.ExecuteAsync(
                "INSERT INTO System_ChangeLog (Id, EntityType, EntityId, InitiatedByType, Action, OccurredAt, DateCreated) " +
                "VALUES (@id, 'quote', @id, 'Seed', 'Created', @now, @now);",
                new { id = Guid.NewGuid().ToString(), now });

            await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
                "INSERT INTO System_ChangeLog (Id, EntityType, EntityId, InitiatedByType, Action, OccurredAt, DateCreated) " +
                "VALUES (@id, 'quote', @id, 'NotARealInitiator', 'Created', @now, @now);",
                new { id = Guid.NewGuid().ToString(), now }));

            await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
                "INSERT INTO System_ChangeLog (Id, EntityType, EntityId, InitiatedByType, Action, OccurredAt, DateCreated) " +
                "VALUES (@id, 'quote', @id, 'Seed', 'NotARealAction', @now, @now);",
                new { id = Guid.NewGuid().ToString(), now }));
        }
    }

    /// <summary>A fresh database with no consumer baseline defined always falls through to the full incremental path, even though it is empty.</summary>
    [TestMethod]
    public async Task ApplyBaselineAsync_NoConsumerBaselineDefined_FallsThroughToIncremental()
    {
        using var temp = new TempDatabase([]);
        var db = CreateBareInitializer(temp.DbPath, []);

        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={temp.DbPath}");
        await conn.OpenAsync();
        var dataRows = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM System_SchemaVersion;");

        Assert.AreEqual(8, dataRows,
            "With no consumer baseline configured, Data's own migrations must still replay incrementally, one row per version");
        Assert.AreEqual(8, db.DataSchemaVersion);
    }

    // ── Ordering proof ────────────────────────────────────────────────────────

    /// <summary>
    /// Direct proof that Quotinator.Data's own migrations always apply before any consumer-supplied
    /// migration: a custom single-entry "consumer" migration list whose SQL would fail with "no such
    /// table" if it ran before Data's own migration 1 (which creates <c>System_AuditEntries</c>) had
    /// a chance to run.
    /// </summary>
    [TestMethod]
    public async Task DataOwnedMigrations_AlwaysApplyBeforeConsumerMigrations()
    {
        using var temp = new TempDatabase([]);
        IReadOnlyList<SchemaMigration> consumerMigrations =
        [
            new SchemaMigration
            {
                Version = 1,
                Sql = "INSERT INTO System_AuditEntries (Id, TableName, Operation, PerformedAt, DateCreated) " +
                      "VALUES (lower(hex(randomblob(16))), 'Probe', 'Inserted', '2026-01-01 00:00:00', '2026-01-01 00:00:00');",
            },
        ];
        var db = CreateBareInitializer(temp.DbPath, consumerMigrations);

        // No exception means the consumer migration's INSERT succeeded — proving System_AuditEntries
        // (created by Data's own migration 1) already existed by the time the consumer migration ran.
        await db.InitialiseAsync();

        using var conn = new SqliteConnection($"Data Source={temp.DbPath}");
        await conn.OpenAsync();
        var probeCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM System_AuditEntries WHERE TableName = 'Probe';");

        Assert.AreEqual(1, probeCount,
            "Consumer migration's INSERT into System_AuditEntries must have succeeded, proving Data's own migrations ran first");
    }

    // ── Reset backup/restore safety net ─────────────────────────────────────────

    // The base DatabaseInitializer's OnResetAsync is a no-op — only a subclass that overrides it
    // (in production, QuotinatorDatabaseInitializer) actually calls DropAndRebuildAsync. This
    // minimal test-only subclass exists purely to exercise that method directly.
    private sealed class ResettableTestInitializer : DatabaseInitializer
    {
        public ResettableTestInitializer(
            IDbConnectionFactory factory, DatabaseOptions options, IReadOnlyList<SchemaMigration> migrations,
            ISystemAuditWriter auditWriter, ICallerContext callerContext, ILogger<DatabaseInitializer> logger)
            : base(factory, options, migrations, auditWriter, callerContext, logger)
        {
        }

        protected override Task OnResetAsync(SqliteConnection connection, bool preserveSchemaVersion, bool forceSourceRefresh)
            => DropAndRebuildAsync(connection, preserveSchemaVersion);
    }

    private static ResettableTestInitializer CreateResettableInitializer(string dbPath, IReadOnlyList<SchemaMigration> consumerMigrations)
    {
        var factory = new SqliteConnectionFactory(dbPath);
        var options = new DatabaseOptions
        {
            DbPath      = dbPath,
            BackupsPath = Path.Combine(Path.GetDirectoryName(dbPath)!, "backups"),
        };
        return new ResettableTestInitializer(factory, options, consumerMigrations,
            NoOpSystemAuditWriter.Instance, NoOpCallerContext.Instance, NullLogger<DatabaseInitializer>.Instance);
    }

    /// <summary>
    /// A genuine, unexpected failure during Reset's migration replay must leave the database exactly
    /// as it was before the Reset attempt — proving the pre-reset backup is actually restored, not
    /// just that the failing transaction rolled back (which alone wouldn't undo the table drop that
    /// already happened before the failing migration ran).
    /// </summary>
    [TestMethod]
    public async Task ResetAsync_MigrationFailsDuringReplay_RestoresPreResetBackupAndRethrows()
    {
        using var temp = new TempDatabase([]);

        IReadOnlyList<SchemaMigration> workingMigrations =
        [
            new SchemaMigration { Version = 1, Sql = "CREATE TABLE IF NOT EXISTS Probe (Id INTEGER); INSERT INTO Probe (Id) VALUES (999);" },
        ];
        var db = CreateResettableInitializer(temp.DbPath, workingMigrations);
        await db.InitialiseAsync();

        // A different, deliberately-broken migration list for the same database file — forces the
        // consumer phase to fail genuinely during Reset's replay (the working table was already
        // dropped by the time this runs).
        IReadOnlyList<SchemaMigration> poisonMigrations =
        [
            new SchemaMigration { Version = 1, Sql = "THIS IS NOT VALID SQL;" },
        ];
        var db2 = CreateResettableInitializer(temp.DbPath, poisonMigrations);

        await Assert.ThrowsExactlyAsync<SqliteException>(() => db2.ResetAsync());

        using var conn = new SqliteConnection($"Data Source={temp.DbPath}");
        await conn.OpenAsync();
        var probeValue = await conn.ExecuteScalarAsync<int>("SELECT Id FROM Probe;");
        Assert.AreEqual(999, probeValue, "Pre-reset data must be fully restored after a failed reset, not left dropped");
    }
}

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
/// a consuming project (e.g. Quotinator.Core) supplies.
/// </summary>
[TestClass]
public class DatabaseInitializerOwnershipTests
{
    private static DatabaseInitializer CreateBareInitializer(
        string dbPath, IReadOnlyList<SchemaMigration> consumerMigrations, SchemaBaseline? baseline = null)
    {
        SqliteConnectionFactory factory = new(dbPath);
        DatabaseOptions options = new()
        {
            DbPath      = dbPath,
            BackupsPath = Path.Combine(Path.GetDirectoryName(dbPath)!, "backups"),
        };
        return new DatabaseInitializer(factory, options, consumerMigrations,
            NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance,
            NullLogger<DatabaseInitializer>.Instance, baseline);
    }

    private static async Task<List<string>> DumpTableSchemaAsync(SqliteConnection conn, string table)
    {
        List<string> lines = [];

        IEnumerable<(int cid, string name, string type, int notnull, string? dflt_value, int pk)> columns = await conn.QueryAsync<(int cid, string name, string type, int notnull, string? dflt_value, int pk)>(
            $"SELECT cid, name, type, [notnull], dflt_value, pk FROM pragma_table_info('{table}');");
        foreach ((int cid, string? name, string? type, int notnull, string? dflt_value, int pk) in columns.OrderBy(c => c.cid))
            lines.Add($"COL {cid} {name} {type} notnull={notnull} default={dflt_value} pk={pk}");

        IEnumerable<(string name, int unique)> indexes = await conn.QueryAsync<(string name, int unique)>(
            $"SELECT name, [unique] FROM pragma_index_list('{table}');");
        foreach ((string? name, int unique) in indexes.OrderBy(i => i.name))
        {
            IEnumerable<(int seqno, string? name)> idxCols = await conn.QueryAsync<(int seqno, string? name)>(
                $"SELECT seqno, name FROM pragma_index_info('{name}');");
            string colList = string.Join(",", idxCols.OrderBy(c => c.seqno).Select(c => c.name));
            lines.Add($"IDX {name} unique={unique} cols=({colList})");
        }

        return lines;
    }

    // ── Data-side schema-drift proof ─────────────────────────────────────────

    /// <summary>
    /// Quotinator.Data's own baseline fragment (<c>DataBaselineSql</c>) must produce the exact same
    /// <c>Audit_Entry</c> schema as replaying Quotinator.Data's own numbered migrations
    /// (<c>DataOwnedMigrations</c>) incrementally. This is what actually enforces "Data's own
    /// scripts stay in sync with each other," independent of whatever consumer exists — exercised
    /// here with zero consumer migrations and a no-op consumer baseline.
    /// </summary>
    [TestMethod]
    public async Task DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemAuditEntriesSchema()
    {
        using TempDatabase tempA = new([]);
        DatabaseInitializer dbA = CreateBareInitializer(tempA.DbPath, [], baseline: new SchemaBaseline { Sql = "SELECT 1;" });
        await dbA.InitialiseAsync();

        using TempDatabase tempB = new([]);
        DatabaseInitializer dbB = CreateBareInitializer(tempB.DbPath, []);
        await dbB.InitialiseForTestingAsync(forceIncremental: true);

        using SqliteConnection connA = new($"Data Source={tempA.DbPath}");
        await connA.OpenAsync(TestContext.CancellationToken);
        using SqliteConnection connB = new($"Data Source={tempB.DbPath}");
        await connB.OpenAsync(TestContext.CancellationToken);

        List<string> schemaA = await DumpTableSchemaAsync(connA, "Audit_Entry");
        List<string> schemaB = await DumpTableSchemaAsync(connB, "Audit_Entry");

        Assert.AreSequenceEqual(schemaB, schemaA, "Audit_Entry schema differs between Data's baseline and incremental paths — " +
            "update DataBaselineSql to match DataOwnedMigrations' final result.");
    }

    /// <summary>
    /// A fresh database carries no application-version history at all — only whatever the running build
    /// records for itself once startup completes.
    /// <para>
    /// #312's migration 9 backfills a <c>1.8.3</c> row on an upgrading database, and that must never
    /// reach a database with no history to speak of. It cannot: a genuinely empty database takes the
    /// one-step baseline path and never replays migrations. This asserts that structural guarantee
    /// rather than trusting it, since the migration itself carries no guard of its own.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task DataOwnedBaseline_FreshDatabase_RecordsNoAppVersionHistory()
    {
        using TempDatabase temp = new([]);
        DatabaseInitializer db = CreateBareInitializer(temp.DbPath, [], baseline: new SchemaBaseline { Sql = "SELECT 1;" });
        await db.InitialiseAsync();

        using SqliteConnection connection = new($"Data Source={temp.DbPath}");
        await connection.OpenAsync(TestContext.CancellationToken);

        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM System_AppVersion;"),
            "A fresh database has no history — a migration's backfill must not be able to invent one for it.");
    }

    /// <summary>
    /// Same proof as <see cref="DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemAuditEntriesSchema"/>,
    /// for <c>Import_Conflict</c> (added by #64's Data-owned migration 3, retrofitted onto
    /// <c>RecordBase</c> by migration 6, and given <c>ExistingBatchId</c> by migration 7 for #149).
    /// </summary>
    [TestMethod]
    public async Task DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemImportConflictsSchema()
    {
        using TempDatabase tempA = new([]);
        DatabaseInitializer dbA = CreateBareInitializer(tempA.DbPath, [], baseline: new SchemaBaseline { Sql = "SELECT 1;" });
        await dbA.InitialiseAsync();

        using TempDatabase tempB = new([]);
        DatabaseInitializer dbB = CreateBareInitializer(tempB.DbPath, []);
        await dbB.InitialiseForTestingAsync(forceIncremental: true);

        using SqliteConnection connA = new($"Data Source={tempA.DbPath}");
        await connA.OpenAsync(TestContext.CancellationToken);
        using SqliteConnection connB = new($"Data Source={tempB.DbPath}");
        await connB.OpenAsync(TestContext.CancellationToken);

        List<string> schemaA = await DumpTableSchemaAsync(connA, "Import_Conflict");
        List<string> schemaB = await DumpTableSchemaAsync(connB, "Import_Conflict");

        Assert.AreSequenceEqual(schemaB, schemaA, "Import_Conflict schema differs between Data's baseline and incremental paths — " +
            "update DataBaselineSql to match DataOwnedMigrations' final result.");
    }

    /// <summary>
    /// Same proof as <see cref="DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemAuditEntriesSchema"/>,
    /// for <c>Audit_Change</c> (added by #56's Data-owned migration 4).
    /// </summary>
    [TestMethod]
    public async Task DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemChangeLogSchema()
    {
        using TempDatabase tempA = new([]);
        DatabaseInitializer dbA = CreateBareInitializer(tempA.DbPath, [], baseline: new SchemaBaseline { Sql = "SELECT 1;" });
        await dbA.InitialiseAsync();

        using TempDatabase tempB = new([]);
        DatabaseInitializer dbB = CreateBareInitializer(tempB.DbPath, []);
        await dbB.InitialiseForTestingAsync(forceIncremental: true);

        using SqliteConnection connA = new($"Data Source={tempA.DbPath}");
        await connA.OpenAsync(TestContext.CancellationToken);
        using SqliteConnection connB = new($"Data Source={tempB.DbPath}");
        await connB.OpenAsync(TestContext.CancellationToken);

        List<string> schemaA = await DumpTableSchemaAsync(connA, "Audit_Change");
        List<string> schemaB = await DumpTableSchemaAsync(connB, "Audit_Change");

        Assert.AreSequenceEqual(schemaB, schemaA, "Audit_Change schema differs between Data's baseline and incremental paths — " +
            "update DataBaselineSql to match DataOwnedMigrations' final result.");
    }

    /// <summary>
    /// Same proof as <see cref="DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemAuditEntriesSchema"/>,
    /// for <c>Import_Action</c> (added by #154's Data-owned migration 8, widened by #165's
    /// migration 10 to add <c>Blocked</c>/<c>MarkCompletenessAs</c>).
    /// </summary>
    [TestMethod]
    public async Task DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemImportActionsSchema()
    {
        using TempDatabase tempA = new([]);
        DatabaseInitializer dbA = CreateBareInitializer(tempA.DbPath, [], baseline: new SchemaBaseline { Sql = "SELECT 1;" });
        await dbA.InitialiseAsync();

        using TempDatabase tempB = new([]);
        DatabaseInitializer dbB = CreateBareInitializer(tempB.DbPath, []);
        await dbB.InitialiseForTestingAsync(forceIncremental: true);

        using SqliteConnection connA = new($"Data Source={tempA.DbPath}");
        await connA.OpenAsync(TestContext.CancellationToken);
        using SqliteConnection connB = new($"Data Source={tempB.DbPath}");
        await connB.OpenAsync(TestContext.CancellationToken);

        List<string> schemaA = await DumpTableSchemaAsync(connA, "Import_Action");
        List<string> schemaB = await DumpTableSchemaAsync(connB, "Import_Action");

        Assert.AreSequenceEqual(schemaB, schemaA, "Import_Action schema differs between Data's baseline and incremental paths — " +
            "update DataBaselineSql to match DataOwnedMigrations' final result.");
    }

    /// <summary>Same drift check as above, for <c>Import_SourceFileOverride</c> (#153).</summary>
    [TestMethod]
    public async Task DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemSourceFileOverridesSchema()
    {
        using TempDatabase tempA = new([]);
        DatabaseInitializer dbA = CreateBareInitializer(tempA.DbPath, [], baseline: new SchemaBaseline { Sql = "SELECT 1;" });
        await dbA.InitialiseAsync();

        using TempDatabase tempB = new([]);
        DatabaseInitializer dbB = CreateBareInitializer(tempB.DbPath, []);
        await dbB.InitialiseForTestingAsync(forceIncremental: true);

        using SqliteConnection connA = new($"Data Source={tempA.DbPath}");
        await connA.OpenAsync(TestContext.CancellationToken);
        using SqliteConnection connB = new($"Data Source={tempB.DbPath}");
        await connB.OpenAsync(TestContext.CancellationToken);

        List<string> schemaA = await DumpTableSchemaAsync(connA, "Import_SourceFileOverride");
        List<string> schemaB = await DumpTableSchemaAsync(connB, "Import_SourceFileOverride");

        Assert.AreSequenceEqual(schemaB, schemaA, "Import_SourceFileOverride schema differs between Data's baseline and incremental paths — " +
            "update DataBaselineSql to match DataOwnedMigrations' final result.");
    }

    /// <summary>
    /// Same proof as <see cref="DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemAuditEntriesSchema"/>,
    /// for all three tables #251's migration 6 introduces together: <c>Import_FileResource</c>,
    /// <c>Import_FileResourceLine</c>, <c>Import_FileResourceBatch</c>.
    /// </summary>
    [TestMethod]
    public async Task DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalFileResourceSchema()
    {
        using TempDatabase tempA = new([]);
        DatabaseInitializer dbA = CreateBareInitializer(tempA.DbPath, [], baseline: new SchemaBaseline { Sql = "SELECT 1;" });
        await dbA.InitialiseAsync();

        using TempDatabase tempB = new([]);
        DatabaseInitializer dbB = CreateBareInitializer(tempB.DbPath, []);
        await dbB.InitialiseForTestingAsync(forceIncremental: true);

        using SqliteConnection connA = new($"Data Source={tempA.DbPath}");
        await connA.OpenAsync(TestContext.CancellationToken);
        using SqliteConnection connB = new($"Data Source={tempB.DbPath}");
        await connB.OpenAsync(TestContext.CancellationToken);

        foreach (string table in new[] { "Import_FileResource", "Import_FileResourceLine", "Import_FileResourceBatch" })
        {
            List<string> schemaA = await DumpTableSchemaAsync(connA, table);
            List<string> schemaB = await DumpTableSchemaAsync(connB, table);

            Assert.AreSequenceEqual(schemaB, schemaA, $"{table} schema differs between Data's baseline and incremental paths — " +
                "update DataBaselineSql to match DataOwnedMigrations' final result.");
        }
    }

    /// <summary>
    /// PRAGMA table_info/index_list do not capture CHECK constraint text — this behavioural round-trip
    /// closes that gap for <c>Import_FileResource.Origin</c>/<c>LineEnding</c>'s enum values (#251,
    /// ADR 008), for both the baseline and incremental paths.
    /// </summary>
    [TestMethod]
    public async Task DataOwnedBaseline_And_IncrementalReplay_AcceptSameFileResourceCheckConstraintValues()
    {
        using TempDatabase tempA = new([]);
        DatabaseInitializer dbA = CreateBareInitializer(tempA.DbPath, [], baseline: new SchemaBaseline { Sql = "SELECT 1;" });
        await dbA.InitialiseAsync();

        using TempDatabase tempB = new([]);
        DatabaseInitializer dbB = CreateBareInitializer(tempB.DbPath, []);
        await dbB.InitialiseForTestingAsync(forceIncremental: true);

        using SqliteConnection connA = new($"Data Source={tempA.DbPath}");
        await connA.OpenAsync(TestContext.CancellationToken);
        using SqliteConnection connB = new($"Data Source={tempB.DbPath}");
        await connB.OpenAsync(TestContext.CancellationToken);

        foreach (SqliteConnection? conn in new[] { connA, connB })
        {
            string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            await conn.ExecuteAsync(
                "INSERT INTO Import_FileResource (Id, FileName, Origin, HomeDirectoryKey, ContentHash, LineEnding, EndsWithTrailingNewline, FirstSeenAtUtc, LastSeenAtUtc, DateCreated) " +
                "VALUES (@id, 'quotinator-curated.json', 'System', 'sources', 'abc123', 'LF', 1, @now, @now, @now);",
                new { id = Guid.NewGuid().ToString(), now });

            await conn.ExecuteAsync(
                "INSERT INTO Import_FileResource (Id, FileName, Origin, HomeDirectoryKey, ContentHash, LineEnding, EndsWithTrailingNewline, FirstSeenAtUtc, LastSeenAtUtc, DateCreated) " +
                "VALUES (@id, 'upload.json', 'Upload', NULL, 'def456', 'CRLF', 0, @now, @now, @now);",
                new { id = Guid.NewGuid().ToString(), now });

            await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
                "INSERT INTO Import_FileResource (Id, FileName, Origin, ContentHash, LineEnding, EndsWithTrailingNewline, FirstSeenAtUtc, LastSeenAtUtc, DateCreated) " +
                "VALUES (@id, 'x.json', 'NotARealOrigin', 'abc123', 'LF', 1, @now, @now, @now);",
                new { id = Guid.NewGuid().ToString(), now }));

            await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
                "INSERT INTO Import_FileResource (Id, FileName, Origin, ContentHash, LineEnding, EndsWithTrailingNewline, FirstSeenAtUtc, LastSeenAtUtc, DateCreated) " +
                "VALUES (@id, 'x.json', 'Bundled', 'abc123', 'LF', 1, @now, @now, @now);",
                new { id = Guid.NewGuid().ToString(), now }), "'Bundled' is the pre-#252 origin value — must be rejected now that the CHECK constraint only accepts 'System'/'User'/'Upload'.");

            await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
                "INSERT INTO Import_FileResource (Id, FileName, Origin, ContentHash, LineEnding, EndsWithTrailingNewline, FirstSeenAtUtc, LastSeenAtUtc, DateCreated) " +
                "VALUES (@id, 'x.json', 'System', 'abc123', 'NotARealLineEnding', 1, @now, @now, @now);",
                new { id = Guid.NewGuid().ToString(), now }));
        }
    }

    /// <summary>
    /// #252's version-7 migration remaps existing pre-generalization rows — proved directly against
    /// version 6's own migration SQL (not the full <see cref="DatabaseInitializer"/> orchestration,
    /// which has no "stop after migration N" test hook) so this exercises exactly the scenario version
    /// 6 edited in place would have silently gotten wrong: a database that already ran version 6 before
    /// version 7 exists. Also proves the migration doesn't break the FK relationship
    /// <c>Import_FileResourceLine</c>/<c>Import_FileResourceBatch</c> hold to <c>Import_FileResource</c>
    /// — the table is dropped and recreated under the same name during the rebuild, which a naive
    /// reading of SQLite's rename-only FK auto-fixup behaviour could raise doubt about (see ADR 015's
    /// own remarks on <c>DomainPrefixRenameMigrations</c> for the *different* scenario where that
    /// fixup genuinely does not apply — a name change, not this migration's same-name rebuild).
    /// </summary>
    [TestMethod]
    public async Task Migration007_RemapsPreGeneralizationOriginValuesAndPreservesChildRowLinks()
    {
        using TempDatabase temp = new([]);
        using SqliteConnection conn = new($"Data Source={temp.DbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);

        await conn.ExecuteAsync("CREATE TABLE IF NOT EXISTS Import_Batch (Id TEXT NOT NULL PRIMARY KEY, Name TEXT, Type TEXT, ImportedAt TEXT, DateCreated TEXT);");
        await conn.ExecuteAsync(FileResourceMigrations.CreateFileResourceTables);

        string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        Guid fileResourceId = Guid.NewGuid();
        Guid batchId = Guid.NewGuid();

        await conn.ExecuteAsync(
            "INSERT INTO Import_Batch (Id, Name, Type, ImportedAt, DateCreated) VALUES (@id, 'test', 'Seed', @now, @now);",
            new { id = batchId.ToString(), now });

        await conn.ExecuteAsync(
            "INSERT INTO Import_FileResource (Id, FileName, Origin, ContentHash, LineEnding, EndsWithTrailingNewline, FirstSeenAtUtc, LastSeenAtUtc, DateCreated) " +
            "VALUES (@id, 'quotinator-curated.json', 'Bundled', 'abc123', 'LF', 1, @now, @now, @now);",
            new { id = fileResourceId.ToString(), now });

        await conn.ExecuteAsync(
            "INSERT INTO Import_FileResourceLine (Id, FileResourceId, LineNumber, Text, DateCreated) VALUES (@id, @fileResourceId, 1, 'line one', @now);",
            new { id = Guid.NewGuid().ToString(), fileResourceId = fileResourceId.ToString(), now });

        await conn.ExecuteAsync(
            "INSERT INTO Import_FileResourceBatch (Id, FileResourceId, ImportBatchId, ImportedAt, DateCreated) VALUES (@id, @fileResourceId, @batchId, @now, @now);",
            new { id = Guid.NewGuid().ToString(), fileResourceId = fileResourceId.ToString(), batchId = batchId.ToString(), now });

        // Advance to version 7. Foreign key enforcement must be off for the rebuild, matching
        // ApplyMigrationsAsync's own PRAGMA foreign_keys toggling around the real migration phase —
        // without this, SQLite treats DROP TABLE Import_FileResource as cascading the DELETE to
        // Import_FileResourceLine/Import_FileResourceBatch (ON DELETE CASCADE), silently losing the
        // rows this test exists to prove survive. Found live by this test's own first run.
        await conn.ExecuteAsync("PRAGMA foreign_keys = OFF;");
        await conn.ExecuteAsync(FileResourceOriginGeneralizationMigrations.GeneralizeOrigin);
        await conn.ExecuteAsync("PRAGMA foreign_keys = ON;");

        (string? origin, string? homeDirectoryKey) = await conn.QuerySingleAsync<(string, string?)>(
            "SELECT Origin, HomeDirectoryKey FROM Import_FileResource WHERE Id = @id;",
            new { id = fileResourceId.ToString() });
        Assert.AreEqual("System", origin, "Pre-#252 'Bundled' rows must be remapped to 'System', not just accepted by a widened CHECK.");
        Assert.AreEqual("sources", homeDirectoryKey, "A remapped System-origin row must backfill HomeDirectoryKey to 'sources' — the only directory 'Bundled' content was ever captured from.");

        int lineCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Import_FileResourceLine WHERE FileResourceId = @id;", new { id = fileResourceId.ToString() });
        Assert.AreEqual(1, lineCount, "Import_FileResourceLine's FK link to the rebuilt Import_FileResource must survive the rebuild.");

        int batchLinkCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Import_FileResourceBatch WHERE FileResourceId = @id;", new { id = fileResourceId.ToString() });
        Assert.AreEqual(1, batchLinkCount, "Import_FileResourceBatch's FK link to the rebuilt Import_FileResource must survive the rebuild.");
    }

    /// <summary>
    /// PRAGMA table_info/index_list do not capture CHECK constraint text — this behavioural round-trip
    /// closes that gap for <c>Import_SourceFileOverride.Origin</c>'s enum values.
    /// </summary>
    [TestMethod]
    public async Task DataOwnedBaseline_And_IncrementalReplay_AcceptSameSourceFileOverridesCheckConstraintValues()
    {
        using TempDatabase tempA = new([]);
        DatabaseInitializer dbA = CreateBareInitializer(tempA.DbPath, [], baseline: new SchemaBaseline { Sql = "SELECT 1;" });
        await dbA.InitialiseAsync();

        using TempDatabase tempB = new([]);
        DatabaseInitializer dbB = CreateBareInitializer(tempB.DbPath, []);
        await dbB.InitialiseForTestingAsync(forceIncremental: true);

        using SqliteConnection connA = new($"Data Source={tempA.DbPath}");
        await connA.OpenAsync(TestContext.CancellationToken);
        using SqliteConnection connB = new($"Data Source={tempB.DbPath}");
        await connB.OpenAsync(TestContext.CancellationToken);

        foreach (SqliteConnection? conn in new[] { connA, connB })
        {
            string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            await conn.ExecuteAsync(
                "INSERT INTO Import_SourceFileOverride (Id, FileName, Origin, ContentHash, DateCreated) " +
                "VALUES (@id, 'vilaboim-conflict-rules.json', 'Bundled', 'abc123', @now);",
                new { id = Guid.NewGuid().ToString(), now });

            await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
                "INSERT INTO Import_SourceFileOverride (Id, FileName, Origin, ContentHash, DateCreated) " +
                "VALUES (@id, 'x.json', 'NotARealOrigin', 'abc123', @now);",
                new { id = Guid.NewGuid().ToString(), now }));
        }
    }

    /// <summary>
    /// PRAGMA table_info/index_list do not capture CHECK constraint text — this behavioural
    /// round-trip closes that gap for <c>Import_Action.Status</c>'s <c>Blocked</c> value,
    /// <c>MarkCompletenessAs</c>'s constraint, and (#150, ADR 008) <c>AppliedPolicy</c>'s constraint,
    /// for both the baseline and incremental paths.
    /// </summary>
    [TestMethod]
    public async Task DataOwnedBaseline_And_IncrementalReplay_AcceptSameImportActionsCheckConstraintValues()
    {
        using TempDatabase tempA = new([]);
        DatabaseInitializer dbA = CreateBareInitializer(tempA.DbPath, [], baseline: new SchemaBaseline { Sql = "SELECT 1;" });
        await dbA.InitialiseAsync();

        using TempDatabase tempB = new([]);
        DatabaseInitializer dbB = CreateBareInitializer(tempB.DbPath, []);
        await dbB.InitialiseForTestingAsync(forceIncremental: true);

        using SqliteConnection connA = new($"Data Source={tempA.DbPath}");
        await connA.OpenAsync(TestContext.CancellationToken);
        using SqliteConnection connB = new($"Data Source={tempB.DbPath}");
        await connB.OpenAsync(TestContext.CancellationToken);

        foreach (SqliteConnection? conn in new[] { connA, connB })
        {
            string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            string id  = Guid.NewGuid().ToString();

            await conn.ExecuteAsync(
                "INSERT INTO Import_Action (Id, BatchId, ActionType, EntityType, EntityId, IncomingValue, AppliedPolicy, Status, MarkCompletenessAs, DetectedAt, DateCreated) " +
                "VALUES (@id, 'B', 'Modify', 'Widget', @id, '{}', 'NewestWins', 'Blocked', 'Complete', @now, @now);",
                new { id, now });

            // #153: Stale must be accepted identically by both paths too — migration 12 widened the
            // CHECK constraint the same way migration 10 widened it for Blocked.
            await conn.ExecuteAsync(
                "INSERT INTO Import_Action (Id, BatchId, ActionType, EntityType, EntityId, IncomingValue, Status, DetectedAt, DateCreated) " +
                "VALUES (@id, 'B', 'Modify', 'Widget', @id, '{}', 'Stale', @now, @now);",
                new { id = Guid.NewGuid().ToString(), now });

            // AppliedPolicy is nullable — a Pending/Blocked action has no policy decided yet.
            await conn.ExecuteAsync(
                "INSERT INTO Import_Action (Id, BatchId, ActionType, EntityType, EntityId, IncomingValue, Status, DetectedAt, DateCreated) " +
                "VALUES (@id, 'B', 'Modify', 'Widget', @id, '{}', 'Pending', @now, @now);",
                new { id = Guid.NewGuid().ToString(), now });

            await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
                "INSERT INTO Import_Action (Id, BatchId, ActionType, EntityType, EntityId, IncomingValue, Status, DetectedAt, DateCreated) " +
                "VALUES (@id, 'B', 'Modify', 'Widget', @id, '{}', 'NotARealStatus', @now, @now);",
                new { id = Guid.NewGuid().ToString(), now }));

            await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
                "INSERT INTO Import_Action (Id, BatchId, ActionType, EntityType, EntityId, IncomingValue, Status, MarkCompletenessAs, DetectedAt, DateCreated) " +
                "VALUES (@id, 'B', 'Modify', 'Widget', @id, '{}', 'Pending', 'NotARealCompletenessValue', @now, @now);",
                new { id = Guid.NewGuid().ToString(), now }));

            await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
                "INSERT INTO Import_Action (Id, BatchId, ActionType, EntityType, EntityId, IncomingValue, AppliedPolicy, Status, DetectedAt, DateCreated) " +
                "VALUES (@id, 'B', 'Modify', 'Widget', @id, '{}', 'NotARealPolicy', 'Pending', @now, @now);",
                new { id = Guid.NewGuid().ToString(), now }));
        }
    }

    /// <summary>
    /// PRAGMA table_info/index_list do not capture CHECK constraint text — this behavioural
    /// round-trip closes that gap for <c>Import_Conflict.AppliedPolicy</c>'s constraint
    /// (#150, ADR 008), for both the baseline and incremental paths.
    /// </summary>
    [TestMethod]
    public async Task DataOwnedBaseline_And_IncrementalReplay_AcceptSameImportConflictsCheckConstraintValues()
    {
        using TempDatabase tempA = new([]);
        DatabaseInitializer dbA = CreateBareInitializer(tempA.DbPath, [], baseline: new SchemaBaseline { Sql = "SELECT 1;" });
        await dbA.InitialiseAsync();

        using TempDatabase tempB = new([]);
        DatabaseInitializer dbB = CreateBareInitializer(tempB.DbPath, []);
        await dbB.InitialiseForTestingAsync(forceIncremental: true);

        using SqliteConnection connA = new($"Data Source={tempA.DbPath}");
        await connA.OpenAsync(TestContext.CancellationToken);
        using SqliteConnection connB = new($"Data Source={tempB.DbPath}");
        await connB.OpenAsync(TestContext.CancellationToken);

        foreach (SqliteConnection? conn in new[] { connA, connB })
        {
            string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            await conn.ExecuteAsync(
                "INSERT INTO Import_Conflict (Id, BatchId, EntityType, AppliedPolicy, Status, DetectedAt, DateCreated) " +
                "VALUES (@id, 'B', 'Quote', 'MergeTheirs', 'Resolved', @now, @now);",
                new { id = Guid.NewGuid().ToString(), now });

            // AppliedPolicy is nullable — a still-Pending conflict has no policy applied yet.
            await conn.ExecuteAsync(
                "INSERT INTO Import_Conflict (Id, BatchId, EntityType, Status, DetectedAt, DateCreated) " +
                "VALUES (@id, 'B', 'Quote', 'Pending', @now, @now);",
                new { id = Guid.NewGuid().ToString(), now });

            await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
                "INSERT INTO Import_Conflict (Id, BatchId, EntityType, AppliedPolicy, Status, DetectedAt, DateCreated) " +
                "VALUES (@id, 'B', 'Quote', 'NotARealPolicy', 'Resolved', @now, @now);",
                new { id = Guid.NewGuid().ToString(), now }));
        }
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
        using TempDatabase tempA = new([]);
        DatabaseInitializer dbA = CreateBareInitializer(tempA.DbPath, [], baseline: new SchemaBaseline { Sql = "SELECT 1;" });
        await dbA.InitialiseAsync();

        using TempDatabase tempB = new([]);
        DatabaseInitializer dbB = CreateBareInitializer(tempB.DbPath, []);
        await dbB.InitialiseForTestingAsync(forceIncremental: true);

        using SqliteConnection connA = new($"Data Source={tempA.DbPath}");
        await connA.OpenAsync(TestContext.CancellationToken);
        using SqliteConnection connB = new($"Data Source={tempB.DbPath}");
        await connB.OpenAsync(TestContext.CancellationToken);

        foreach (SqliteConnection? conn in new[] { connA, connB })
        {
            string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            await conn.ExecuteAsync(
                "INSERT INTO Audit_Change (Id, EntityType, EntityId, InitiatedByType, Action, OccurredAt, DateCreated) " +
                "VALUES (@id, 'quote', @id, 'Seed', 'Created', @now, @now);",
                new { id = Guid.NewGuid().ToString(), now });

            await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
                "INSERT INTO Audit_Change (Id, EntityType, EntityId, InitiatedByType, Action, OccurredAt, DateCreated) " +
                "VALUES (@id, 'quote', @id, 'NotARealInitiator', 'Created', @now, @now);",
                new { id = Guid.NewGuid().ToString(), now }));

            await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
                "INSERT INTO Audit_Change (Id, EntityType, EntityId, InitiatedByType, Action, OccurredAt, DateCreated) " +
                "VALUES (@id, 'quote', @id, 'Seed', 'NotARealAction', @now, @now);",
                new { id = Guid.NewGuid().ToString(), now }));
        }
    }

    /// <summary>
    /// Same proof as <see cref="DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemAuditEntriesSchema"/>,
    /// for <c>System_Notification</c> (added by #278's Data-owned migration 8).
    /// </summary>
    [TestMethod]
    public async Task DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemNotificationSchema()
    {
        using TempDatabase tempA = new([]);
        DatabaseInitializer dbA = CreateBareInitializer(tempA.DbPath, [], baseline: new SchemaBaseline { Sql = "SELECT 1;" });
        await dbA.InitialiseAsync();

        using TempDatabase tempB = new([]);
        DatabaseInitializer dbB = CreateBareInitializer(tempB.DbPath, []);
        await dbB.InitialiseForTestingAsync(forceIncremental: true);

        using SqliteConnection connA = new($"Data Source={tempA.DbPath}");
        await connA.OpenAsync(TestContext.CancellationToken);
        using SqliteConnection connB = new($"Data Source={tempB.DbPath}");
        await connB.OpenAsync(TestContext.CancellationToken);

        List<string> schemaA = await DumpTableSchemaAsync(connA, "System_Notification");
        List<string> schemaB = await DumpTableSchemaAsync(connB, "System_Notification");

        Assert.AreSequenceEqual(schemaB, schemaA, "System_Notification schema differs between Data's baseline and incremental paths — " +
            "update DataBaselineSql to match DataOwnedMigrations' final result.");
    }

    /// <summary>
    /// Same proof as <see cref="DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemNotificationSchema"/>,
    /// for <c>System_NotificationTranslation</c> (added by #319's Data-owned migration). The parity
    /// test compares column ordinals, which is why a column added by <c>ALTER TABLE … ADD COLUMN</c>
    /// must trail in the baseline rather than sit where it reads most naturally.
    /// </summary>
    [TestMethod]
    public async Task DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemNotificationTranslationSchema()
    {
        using TempDatabase tempA = new([]);
        DatabaseInitializer dbA = CreateBareInitializer(tempA.DbPath, [], baseline: new SchemaBaseline { Sql = "SELECT 1;" });
        await dbA.InitialiseAsync();

        using TempDatabase tempB = new([]);
        DatabaseInitializer dbB = CreateBareInitializer(tempB.DbPath, []);
        await dbB.InitialiseForTestingAsync(forceIncremental: true);

        using SqliteConnection connA = new($"Data Source={tempA.DbPath}");
        await connA.OpenAsync(TestContext.CancellationToken);
        using SqliteConnection connB = new($"Data Source={tempB.DbPath}");
        await connB.OpenAsync(TestContext.CancellationToken);

        List<string> schemaA = await DumpTableSchemaAsync(connA, "System_NotificationTranslation");
        List<string> schemaB = await DumpTableSchemaAsync(connB, "System_NotificationTranslation");

        Assert.IsNotEmpty(schemaA, "System_NotificationTranslation is missing from Data's baseline path.");
        Assert.AreSequenceEqual(schemaB, schemaA, "System_NotificationTranslation schema differs between Data's baseline and incremental paths — " +
            "update DataBaselineSql to match DataOwnedMigrations' final result.");
    }

    /// <summary>
    /// <c>System_Notification.OriginalLanguage</c> reaches the same shape on both paths — a column
    /// added by <c>ALTER TABLE</c> is the exact case where the baseline silently drifts.
    /// </summary>
    [TestMethod]
    public async Task DataOwnedBaseline_And_IncrementalReplay_AgreeOnNotificationOriginalLanguage()
    {
        using TempDatabase tempA = new([]);
        DatabaseInitializer dbA = CreateBareInitializer(tempA.DbPath, [], baseline: new SchemaBaseline { Sql = "SELECT 1;" });
        await dbA.InitialiseAsync();

        using TempDatabase tempB = new([]);
        DatabaseInitializer dbB = CreateBareInitializer(tempB.DbPath, []);
        await dbB.InitialiseForTestingAsync(forceIncremental: true);

        using SqliteConnection connA = new($"Data Source={tempA.DbPath}");
        await connA.OpenAsync(TestContext.CancellationToken);
        using SqliteConnection connB = new($"Data Source={tempB.DbPath}");
        await connB.OpenAsync(TestContext.CancellationToken);

        string? defaultA = await connA.ExecuteScalarAsync<string>(
            "SELECT dflt_value FROM pragma_table_info('System_Notification') WHERE name = 'OriginalLanguage';");
        string? defaultB = await connB.ExecuteScalarAsync<string>(
            "SELECT dflt_value FROM pragma_table_info('System_Notification') WHERE name = 'OriginalLanguage';");

        Assert.IsNotNull(defaultA, "System_Notification.OriginalLanguage is missing from Data's baseline path.");
        Assert.AreEqual(defaultB, defaultA, "OriginalLanguage's default differs between the baseline and incremental paths.");
    }

    /// <summary>
    /// PRAGMA table_info/index_list do not capture CHECK constraint text — this behavioural round-trip
    /// closes that gap for <c>System_Notification.Type</c>/<c>DismissTriggerKey</c>'s enum values
    /// (#278, ADR 008), for both the baseline and incremental paths.
    /// </summary>
    [TestMethod]
    public async Task DataOwnedBaseline_And_IncrementalReplay_AcceptSameNotificationCheckConstraintValues()
    {
        using TempDatabase tempA = new([]);
        DatabaseInitializer dbA = CreateBareInitializer(tempA.DbPath, [], baseline: new SchemaBaseline { Sql = "SELECT 1;" });
        await dbA.InitialiseAsync();

        using TempDatabase tempB = new([]);
        DatabaseInitializer dbB = CreateBareInitializer(tempB.DbPath, []);
        await dbB.InitialiseForTestingAsync(forceIncremental: true);

        using SqliteConnection connA = new($"Data Source={tempA.DbPath}");
        await connA.OpenAsync(TestContext.CancellationToken);
        using SqliteConnection connB = new($"Data Source={tempB.DbPath}");
        await connB.OpenAsync(TestContext.CancellationToken);

        foreach (SqliteConnection? conn in new[] { connA, connB })
        {
            string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            await conn.ExecuteAsync(
                "INSERT INTO System_Notification (Id, Type, Body, DismissTriggerKey, DateCreated) " +
                "VALUES (@id, 'ActionRequired', 'Consider running a Reset.', 'DatabaseReset', @now);",
                new { id = Guid.NewGuid().ToString(), now });

            // DismissTriggerKey is nullable — most notifications carry no dismiss trigger.
            await conn.ExecuteAsync(
                "INSERT INTO System_Notification (Id, Type, Body, DateCreated) " +
                "VALUES (@id, 'Information', 'Just letting you know.', @now);",
                new { id = Guid.NewGuid().ToString(), now });

            await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
                "INSERT INTO System_Notification (Id, Type, Body, DateCreated) " +
                "VALUES (@id, 'NotARealType', 'x', @now);",
                new { id = Guid.NewGuid().ToString(), now }));

            await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
                "INSERT INTO System_Notification (Id, Type, Body, DismissTriggerKey, DateCreated) " +
                "VALUES (@id, 'Information', 'x', 'NotARealTrigger', @now);",
                new { id = Guid.NewGuid().ToString(), now }));
        }
    }

    /// <summary>
    /// Same proof as <see cref="DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemAuditEntriesSchema"/>,
    /// for <c>System_AppVersion</c> (added by #81's Data-owned migration 4).
    /// </summary>
    [TestMethod]
    public async Task DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemAppVersionSchema()
    {
        using TempDatabase tempA = new([]);
        DatabaseInitializer dbA = CreateBareInitializer(tempA.DbPath, [], baseline: new SchemaBaseline { Sql = "SELECT 1;" });
        await dbA.InitialiseAsync();

        using TempDatabase tempB = new([]);
        DatabaseInitializer dbB = CreateBareInitializer(tempB.DbPath, []);
        await dbB.InitialiseForTestingAsync(forceIncremental: true);

        using SqliteConnection connA = new($"Data Source={tempA.DbPath}");
        await connA.OpenAsync(TestContext.CancellationToken);
        using SqliteConnection connB = new($"Data Source={tempB.DbPath}");
        await connB.OpenAsync(TestContext.CancellationToken);

        List<string> schemaA = await DumpTableSchemaAsync(connA, "System_AppVersion");
        List<string> schemaB = await DumpTableSchemaAsync(connB, "System_AppVersion");

        Assert.AreSequenceEqual(schemaB, schemaA, "System_AppVersion schema differs between Data's baseline and incremental paths — " +
            "update DataBaselineSql to match DataOwnedMigrations' final result.");
    }

    /// <summary>A fresh database with no consumer baseline defined always falls through to the full incremental path, even though it is empty.</summary>
    [TestMethod]
    public async Task ApplyBaselineAsync_NoConsumerBaselineDefined_FallsThroughToIncremental()
    {
        using TempDatabase temp = new([]);
        DatabaseInitializer db = CreateBareInitializer(temp.DbPath, []);

        await db.InitialiseAsync();

        using SqliteConnection conn = new($"Data Source={temp.DbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);
        int dataRows = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM System_SchemaVersion;");

        Assert.AreEqual(14, dataRows,
            "With no consumer baseline configured, Data's own migrations must still replay incrementally, one row per version");
        Assert.AreEqual(14, db.DataSchemaVersion);
    }

    // ── Ordering proof ────────────────────────────────────────────────────────

    /// <summary>
    /// Direct proof that Quotinator.Data's own migrations always apply before any consumer-supplied
    /// migration: a custom single-entry "consumer" migration list whose SQL would fail with "no such
    /// table" if it ran before Data's own migration 1 (which creates <c>Audit_Entry</c>) had
    /// a chance to run.
    /// </summary>
    [TestMethod]
    public async Task DataOwnedMigrations_AlwaysApplyBeforeConsumerMigrations()
    {
        using TempDatabase temp = new([]);
        IReadOnlyList<SchemaMigration> consumerMigrations =
        [
            new SchemaMigration
            {
                Version = 1,
                Sql = "INSERT INTO Audit_Entry (Id, TableName, Operation, PerformedAt, DateCreated) " +
                      "VALUES (lower(hex(randomblob(16))), 'Probe', 'Inserted', '2026-01-01 00:00:00', '2026-01-01 00:00:00');",
            },
        ];
        DatabaseInitializer db = CreateBareInitializer(temp.DbPath, consumerMigrations);

        // No exception means the consumer migration's INSERT succeeded — proving Audit_Entry
        // (created by Data's own migration 1) already existed by the time the consumer migration ran.
        await db.InitialiseAsync();

        using SqliteConnection conn = new($"Data Source={temp.DbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);
        int probeCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Audit_Entry WHERE TableName = 'Probe';");

        Assert.AreEqual(1, probeCount,
            "Consumer migration's INSERT into Audit_Entry must have succeeded, proving Data's own migrations ran first");
    }

    // ── Reset backup/restore safety net ─────────────────────────────────────────

    // The base DatabaseInitializer's OnResetAsync is a no-op — only a subclass that overrides it
    // (in production, QuotinatorDatabaseInitializer) actually calls DropAndRebuildAsync. This
    // minimal test-only subclass exists purely to exercise that method directly.
    private sealed class ResettableTestInitializer(
        IDbConnectionFactory factory, DatabaseOptions options, IReadOnlyList<SchemaMigration> migrations,
        IAuditEntryWriter auditWriter, ICallerContext callerContext, ILogger<DatabaseInitializer> logger) : DatabaseInitializer(factory, options, migrations, auditWriter, callerContext, logger)
    {
        protected override Task OnResetAsync(SqliteConnection connection, bool preserveSchemaVersion, bool forceSourceRefresh)
            => DropAndRebuildAsync(connection, preserveSchemaVersion);
    }

    private static ResettableTestInitializer CreateResettableInitializer(string dbPath, IReadOnlyList<SchemaMigration> consumerMigrations)
    {
        SqliteConnectionFactory factory = new(dbPath);
        DatabaseOptions options = new()
        {
            DbPath      = dbPath,
            BackupsPath = Path.Combine(Path.GetDirectoryName(dbPath)!, "backups"),
        };
        return new ResettableTestInitializer(factory, options, consumerMigrations,
            NoOpAuditEntryWriter.Instance, NoOpCallerContext.Instance, NullLogger<DatabaseInitializer>.Instance);
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
        using TempDatabase temp = new([]);

        IReadOnlyList<SchemaMigration> workingMigrations =
        [
            new SchemaMigration { Version = 1, Sql = "CREATE TABLE IF NOT EXISTS Probe (Id INTEGER); INSERT INTO Probe (Id) VALUES (999);" },
        ];
        ResettableTestInitializer db = CreateResettableInitializer(temp.DbPath, workingMigrations);
        await db.InitialiseAsync();

        // A different, deliberately-broken migration list for the same database file — forces the
        // consumer phase to fail genuinely during Reset's replay (the working table was already
        // dropped by the time this runs).
        IReadOnlyList<SchemaMigration> poisonMigrations =
        [
            new SchemaMigration { Version = 1, Sql = "THIS IS NOT VALID SQL;" },
        ];
        ResettableTestInitializer db2 = CreateResettableInitializer(temp.DbPath, poisonMigrations);

        await Assert.ThrowsExactlyAsync<SqliteException>(() => db2.ResetAsync());

        using SqliteConnection conn = new($"Data Source={temp.DbPath}");
        await conn.OpenAsync(TestContext.CancellationToken);
        int probeValue = await conn.ExecuteScalarAsync<int>("SELECT Id FROM Probe;");
        Assert.AreEqual(999, probeValue, "Pre-reset data must be fully restored after a failed reset, not left dropped");
    }

    public TestContext TestContext { get; set; }
}

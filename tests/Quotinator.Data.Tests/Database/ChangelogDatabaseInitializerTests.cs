using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;

namespace Quotinator.Data.Tests.Database;

/// <summary>
/// Proves <see cref="ChangelogDatabaseInitializer"/>'s own baseline-vs-incremental schema-drift
/// parity (#309, ADR 018) — the same class of proof
/// <see cref="DatabaseInitializerOwnershipTests"/> already applies to the main database's own
/// <see cref="DatabaseInitializer"/>, applied here to the separate changelog database.
/// </summary>
[TestClass]
public class ChangelogDatabaseInitializerTests
{
    [TestCleanup]
    public void TestCleanup() => SqliteConnection.ClearAllPools();

    private static string UniqueConnectionString() =>
        $"file:{Guid.NewGuid():N}?mode=memory&cache=shared";

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

    /// <summary>
    /// <see cref="ChangelogDatabaseInitializer.InitialiseAsync()"/>'s baseline path (a genuinely
    /// empty database) and <see cref="ChangelogDatabaseInitializer.InitialiseForTestingAsync"/>'s
    /// forced incremental path must produce byte-for-byte identical <c>Changelog_Entry</c>/<c>Changelog_Line</c>
    /// schemas — otherwise <c>BaselineSql</c> has drifted from <c>Migrations</c>' final result.
    /// </summary>
    [TestMethod]
    public async Task Baseline_And_IncrementalReplay_ProduceIdenticalSchema()
    {
        SqliteConnectionFactory factoryA = new SqliteConnectionFactory(UniqueConnectionString());
        using ChangelogConnectionKeepAlive keepAliveA = new ChangelogConnectionKeepAlive(factoryA);
        ChangelogDatabaseInitializer dbA = new ChangelogDatabaseInitializer(factoryA, NullLogger<ChangelogDatabaseInitializer>.Instance);
        await dbA.InitialiseAsync();

        SqliteConnectionFactory factoryB = new SqliteConnectionFactory(UniqueConnectionString());
        using ChangelogConnectionKeepAlive keepAliveB = new ChangelogConnectionKeepAlive(factoryB);
        ChangelogDatabaseInitializer dbB = new ChangelogDatabaseInitializer(factoryB, NullLogger<ChangelogDatabaseInitializer>.Instance);
        await dbB.InitialiseForTestingAsync(forceIncremental: true);

        using SqliteConnection connA = (SqliteConnection)factoryA.CreateConnection();
        await connA.OpenAsync(TestContext.CancellationToken);
        using SqliteConnection connB = (SqliteConnection)factoryB.CreateConnection();
        await connB.OpenAsync(TestContext.CancellationToken);

        foreach (string? table in new[] { "Changelog_Entry", "Changelog_Line" })
        {
            List<string> schemaA = await DumpTableSchemaAsync(connA, table);
            List<string> schemaB = await DumpTableSchemaAsync(connB, table);

            Assert.AreSequenceEqual(schemaB, schemaA, $"{table} schema differs between the changelog database's baseline and incremental paths — " +
                "update ChangelogDatabaseInitializer.BaselineSql to match Migrations' final result.");
        }
    }

    /// <summary>
    /// PRAGMA table_info/index_list do not capture CHECK constraint text — this behavioural
    /// round-trip closes that gap for <c>Changelog_Line.Kind</c>'s enum values, for both the
    /// baseline and incremental paths.
    /// </summary>
    [TestMethod]
    public async Task Baseline_And_IncrementalReplay_AcceptSameKindCheckConstraintValues()
    {
        SqliteConnectionFactory factoryA = new SqliteConnectionFactory(UniqueConnectionString());
        using ChangelogConnectionKeepAlive keepAliveA = new ChangelogConnectionKeepAlive(factoryA);
        ChangelogDatabaseInitializer dbA = new ChangelogDatabaseInitializer(factoryA, NullLogger<ChangelogDatabaseInitializer>.Instance);
        await dbA.InitialiseAsync();

        SqliteConnectionFactory factoryB = new SqliteConnectionFactory(UniqueConnectionString());
        using ChangelogConnectionKeepAlive keepAliveB = new ChangelogConnectionKeepAlive(factoryB);
        ChangelogDatabaseInitializer dbB = new ChangelogDatabaseInitializer(factoryB, NullLogger<ChangelogDatabaseInitializer>.Instance);
        await dbB.InitialiseForTestingAsync(forceIncremental: true);

        using SqliteConnection connA = (SqliteConnection)factoryA.CreateConnection();
        await connA.OpenAsync(TestContext.CancellationToken);
        using SqliteConnection connB = (SqliteConnection)factoryB.CreateConnection();
        await connB.OpenAsync(TestContext.CancellationToken);

        foreach (SqliteConnection? conn in new[] { connA, connB })
        {
            string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            await conn.ExecuteAsync(
                "INSERT INTO Changelog_Entry (Id, Language, Version, DateCreated) VALUES (@id, 'en', '1.9.0', @now);",
                new { id = Guid.NewGuid().ToString(), now });
            string? changelogId = await conn.ExecuteScalarAsync<string>("SELECT Id FROM Changelog_Entry LIMIT 1;");

            await conn.ExecuteAsync(
                "INSERT INTO Changelog_Line (Id, ChangelogEntryId, Kind, Value, SortOrder, DateCreated) " +
                "VALUES (@id, @changelogId, 'Highlight', 'Something changed.', 0, @now);",
                new { id = Guid.NewGuid().ToString(), changelogId, now });

            await Assert.ThrowsExactlyAsync<SqliteException>(() => conn.ExecuteAsync(
                "INSERT INTO Changelog_Line (Id, ChangelogEntryId, Kind, Value, SortOrder, DateCreated) " +
                "VALUES (@id, @changelogId, 'NotARealKind', 'x', 1, @now);",
                new { id = Guid.NewGuid().ToString(), changelogId, now }));
        }
    }

    /// <summary>A genuinely empty changelog database takes the one-step baseline path, not incremental replay.</summary>
    [TestMethod]
    public async Task EmptyDatabase_AppliesBaseline()
    {
        SqliteConnectionFactory factory = new SqliteConnectionFactory(UniqueConnectionString());
        using ChangelogConnectionKeepAlive keepAlive = new ChangelogConnectionKeepAlive(factory);
        ChangelogDatabaseInitializer db = new ChangelogDatabaseInitializer(factory, NullLogger<ChangelogDatabaseInitializer>.Instance);

        await db.InitialiseAsync();

        using SqliteConnection conn = (SqliteConnection)factory.CreateConnection();
        await conn.OpenAsync(TestContext.CancellationToken);
        int versionRows = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Changelog_SchemaVersion;");

        Assert.AreEqual(1, versionRows,
            "The baseline path records exactly one version row (the final version), not one row per migration.");
        Assert.AreEqual(1, db.SchemaVersion);
    }

    public TestContext TestContext { get; set; }
}

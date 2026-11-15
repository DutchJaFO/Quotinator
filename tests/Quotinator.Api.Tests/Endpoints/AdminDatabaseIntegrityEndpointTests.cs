using System.Data;
using System.Net;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Paths;
using Quotinator.Data.Testing.Database;

namespace Quotinator.Api.Tests.Endpoints;

/// <summary>
/// #305: <c>GET /api/v1/admin/database/integrity</c> runs the integrity check on every database when asked
/// and reports what it found. It is read-only, so it sits with the other read-only admin endpoints and
/// needs no key; it answers while the application is degraded, which is when it is most useful; and it
/// never changes health.
/// <para>
/// Real databases in a real data directory, as <c>StartupResilienceTests</c> uses: the endpoint's value is
/// what it finds in an actual schema, which a fake initializer cannot show.
/// </para>
/// </summary>
[TestClass]
public class AdminDatabaseIntegrityEndpointTests
{
    private const string IntegrityRoute = "/api/v1/admin/database/integrity";
    private const string HealthRoute = "/api/v1/health";

    private readonly List<TempDirectory> _temporaryDirectories = [];

    [TestCleanup]
    public void RemoveTemporaryDirectories()
    {
        SqliteConnection.ClearAllPools();
        foreach (TempDirectory directory in _temporaryDirectories)
            directory.Dispose();
    }

    [TestMethod]
    public async Task IntactDatabases_ReportIntact()
    {
        using WebApplicationFactory<Program> factory = FactoryFor(NewDataDirectory());
        using HttpClient client = factory.CreateClient();

        JsonElement databases = await GetDatabasesAsync(client);

        Assert.AreSequenceEqual(["main", "changelog"],
            [.. databases.EnumerateArray().Select(d => d.GetProperty("database").GetString())],
            SequenceOrder.InAnyOrder, "every database the application has is reported");
        foreach (JsonElement database in databases.EnumerateArray())
        {
            Assert.IsTrue(database.GetProperty("isIntact").GetBoolean(), $"{database.GetProperty("database")} is not intact");
            Assert.AreEqual(0, database.GetProperty("differences").GetArrayLength());
        }
    }

    [TestMethod]
    public async Task MissingTable_ReportsTheDifference()
    {
        using WebApplicationFactory<Program> factory = FactoryWithSabotage("DROP TABLE System_Notification;");
        using HttpClient client = factory.CreateClient();

        JsonElement main = Database(await GetDatabasesAsync(client), "main");

        Assert.IsFalse(main.GetProperty("isIntact").GetBoolean());
        Assert.Contains(d =>
            d.GetProperty("kind").GetString() == "missing"
            && d.GetProperty("objectKind").GetString() == "table"
            && d.GetProperty("objectName").GetString() == "System_Notification",
            main.GetProperty("differences").EnumerateArray());
    }

    /// <summary>
    /// The column is added while the application runs, after startup's own check, so reporting it proves
    /// the endpoint checks when asked rather than returning the startup result. An extra does not degrade.
    /// </summary>
    [TestMethod]
    public async Task ExtraColumn_ReportedButIntact()
    {
        string dataDirectory = NewDataDirectory();
        using WebApplicationFactory<Program> factory = FactoryFor(dataDirectory);
        using HttpClient client = factory.CreateClient();
        await GetDatabasesAsync(client);

        Execute(dataDirectory, "ALTER TABLE System_Notification ADD COLUMN Stray TEXT;");
        JsonElement main = Database(await GetDatabasesAsync(client), "main");

        Assert.IsTrue(main.GetProperty("isIntact").GetBoolean(), "an extra column is reported, never a failure");
        Assert.Contains(d =>
            d.GetProperty("kind").GetString() == "extra"
            && d.GetProperty("objectKind").GetString() == "column"
            && d.GetProperty("objectName").GetString() == "System_Notification.Stray",
            main.GetProperty("differences").EnumerateArray());
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync(HealthRoute, TestContext.CancellationToken)).StatusCode);
    }

    [TestMethod]
    public async Task Degraded_EndpointStillAnswers()
    {
        using WebApplicationFactory<Program> factory = FactoryWithSabotage("DROP TABLE System_Notification;");
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage health = await client.GetAsync(HealthRoute, TestContext.CancellationToken);
        HttpResponseMessage integrity = await client.GetAsync(IntegrityRoute, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, health.StatusCode, "precondition: the application degraded");
        Assert.AreEqual(HttpStatusCode.OK, integrity.StatusCode, "200 means the check ran, whatever it found");
    }

    /// <summary>
    /// Reporting is the endpoint's one job: it never marks health either way. A degraded application stays
    /// degraded after a check, and a healthy one stays healthy.
    /// </summary>
    [TestMethod]
    public async Task Check_DoesNotChangeHealth()
    {
        using WebApplicationFactory<Program> degraded = FactoryWithSabotage("DROP TABLE System_Notification;");
        using HttpClient degradedClient = degraded.CreateClient();
        await GetDatabasesAsync(degradedClient);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable,
            (await degradedClient.GetAsync(HealthRoute, TestContext.CancellationToken)).StatusCode);

        using WebApplicationFactory<Program> healthy = FactoryFor(NewDataDirectory());
        using HttpClient healthyClient = healthy.CreateClient();
        await GetDatabasesAsync(healthyClient);
        Assert.AreEqual(HttpStatusCode.OK,
            (await healthyClient.GetAsync(HealthRoute, TestContext.CancellationToken)).StatusCode);
    }

    [TestMethod]
    public async Task WithoutAdminKey_IsAnswered()
    {
        using WebApplicationFactory<Program> factory = FactoryFor(NewDataDirectory());
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(IntegrityRoute, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, "a read-only admin endpoint needs no key");
        Assert.AreEqual(2, (await GetDatabasesAsync(client)).GetArrayLength(), "control: the keyless answer carries the report");
    }

    /// <summary>
    /// #490, the positive control for row 18: both databases report every layer, in order, each passed, and are
    /// sound.
    /// </summary>
    [TestMethod]
    public async Task IntactDatabases_ReportEveryLayerPassed()
    {
        using WebApplicationFactory<Program> factory = FactoryFor(NewDataDirectory());
        using HttpClient client = factory.CreateClient();

        foreach (JsonElement database in (await GetDatabasesAsync(client)).EnumerateArray())
        {
            string name = database.GetProperty("database").GetString()!;
            Assert.IsTrue(database.GetProperty("isSound").GetBoolean(), $"{name} is not sound");
            Assert.AreSequenceEqual(AllLayers, Layers(database).Select(l => l.Layer), $"{name}: every layer, in order");
            Assert.IsTrue(Layers(database).All(l => l.Status == "passed"), $"{name}: {string.Join(", ", Layers(database))}");
        }
    }

    /// <summary>
    /// #490, row 18: a main database file that is not a database used to make the endpoint throw. It now reports
    /// the failing layer: Format fails, nothing after it runs, and with no readable schema there is nothing to
    /// compare, so it is not intact and lists no differences.
    /// </summary>
    [TestMethod]
    public async Task Integrity_CorruptDatabase_ReportsTheFailingLayerInsteadOfThrowing()
    {
        string dataDirectory = NewDataDirectory();
        File.WriteAllText(Path.Combine(dataDirectory, DataPaths.DatabaseFile), "this file is not a SQLite database");
        using WebApplicationFactory<Program> factory = FactoryFor(dataDirectory);
        using HttpClient client = factory.CreateClient();

        JsonElement main = Database(await GetDatabasesAsync(client), "main");

        Assert.IsFalse(main.GetProperty("isSound").GetBoolean());
        Assert.AreEqual("failed", Layers(main).Single(l => l.Layer == "format").Status);
        Assert.IsTrue(Layers(main).SkipWhile(l => l.Layer != "format").Skip(1).All(l => l.Status == "notrun"),
            string.Join(", ", Layers(main)));
        Assert.IsFalse(main.GetProperty("isIntact").GetBoolean(), "A schema that could not be read was reported intact.");
        Assert.AreEqual(0, main.GetProperty("differences").GetArrayLength());
    }

    /// <summary>
    /// #490, row 18: the endpoint always checks at <c>Full</c>. With the startup setting at <c>Fast</c>, Indexes and
    /// References are still checked, never reported skipped.
    /// </summary>
    [TestMethod]
    public async Task Integrity_FastSetting_StillReportsEveryLayer()
    {
        using WebApplicationFactory<Program> factory = FactoryFor(NewDataDirectory(), ("Quotinator:DatabaseStartupCheck", "fast"));
        using HttpClient client = factory.CreateClient();

        foreach (JsonElement database in (await GetDatabasesAsync(client)).EnumerateArray())
        {
            Assert.AreEqual("passed", Layers(database).Single(l => l.Layer == "indexes").Status);
            Assert.AreEqual("passed", Layers(database).Single(l => l.Layer == "references").Status);
        }
    }

    /// <summary>
    /// #490: an index that disagrees with its table fails the Indexes layer while the schema still matches, so the
    /// database is reported not sound and still intact: the two answer different questions (decision B).
    /// </summary>
    [TestMethod]
    public async Task Integrity_IndexDisagreesWithItsTable_ReportsTheLayerAndStaysIntact()
    {
        string dataDirectory = NewDataDirectory();
        using (WebApplicationFactory<Program> firstRun = FactoryFor(dataDirectory))
        {
            using HttpClient warmUp = firstRun.CreateClient();
        }

        SqliteConnection.ClearAllPools();
        string database = Path.Combine(dataDirectory, DataPaths.DatabaseFile);
        File.Delete(database + "-wal");
        File.Delete(database + "-shm");
        DocumentedFormatFixtures.Execute(database,
            "CREATE TABLE CheckFixture (Id INTEGER PRIMARY KEY, Rank INTEGER NOT NULL);",
            "CREATE INDEX IX_CheckFixture_Rank ON CheckFixture (Rank);",
            "INSERT INTO CheckFixture (Id, Rank) VALUES (1, 10), (2, 20), (3, 30), (4, 40), (5, 50);");
        DocumentedFormatFixtures.RewriteLastIndexKey(database, "IX_CheckFixture_Rank", 100);

        using WebApplicationFactory<Program> factory = FactoryFor(dataDirectory);
        using HttpClient client = factory.CreateClient();
        JsonElement main = Database(await GetDatabasesAsync(client), "main");

        Assert.IsFalse(main.GetProperty("isSound").GetBoolean());
        Assert.AreEqual("failed", Layers(main).Single(l => l.Layer == "indexes").Status);
        Assert.IsTrue(main.GetProperty("isIntact").GetBoolean(), "The schema matches; only an index's content is wrong.");
    }

    private static readonly string[] AllLayers = ["storage", "format", "journal", "pages", "indexes", "references", "schema"];

    private static IEnumerable<(string Layer, string Status)> Layers(JsonElement database) =>
        database.GetProperty("layers").EnumerateArray()
            .Select(l => (l.GetProperty("layer").GetString()!, l.GetProperty("status").GetString()!));

    private async Task<JsonElement> GetDatabasesAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.GetAsync(IntegrityRoute, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
        return document.RootElement.GetProperty("databases").Clone();
    }

    private static JsonElement Database(JsonElement databases, string name) =>
        databases.EnumerateArray().Single(d => d.GetProperty("database").GetString() == name);

    private WebApplicationFactory<Program> FactoryWithSabotage(string sabotage)
    {
        string dataDirectory = NewDataDirectory();
        using (WebApplicationFactory<Program> firstRun = FactoryFor(dataDirectory))
        {
            using HttpClient warmUp = firstRun.CreateClient();
        }

        SqliteConnection.ClearAllPools();
        Execute(dataDirectory, sabotage);
        return FactoryFor(dataDirectory);
    }

    /// <summary>
    /// Changes the schema outside any migration. Foreign keys are off for this connection only: a first run
    /// wrote rows that reference the table being dropped, and the point is a missing object, not a refused drop.
    /// </summary>
    private static void Execute(string dataDirectory, string sql)
    {
        using (IDbConnection connection =
            new SqliteConnectionFactory(Path.Combine(dataDirectory, DataPaths.DatabaseFile)).CreateConnection())
        {
            connection.Execute("PRAGMA foreign_keys = OFF;");
            connection.Execute(sql);
        }

        SqliteConnection.ClearAllPools();
    }

    private static WebApplicationFactory<Program> FactoryFor(string dataDirectory, params (string Key, string Value)[] settings) =>
        new QuotinatorWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Quotinator:DataDir", dataDirectory);
            foreach ((string key, string value) in settings)
                builder.UseSetting(key, value);
        });

    private string NewDataDirectory()
    {
        TempDirectory directory = new("quotinator_305_endpoint_");
        _temporaryDirectories.Add(directory);
        return directory.Path;
    }

    public TestContext TestContext { get; set; }
}

using System.Data;
using System.Net;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Startup;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Paths;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.Database;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// #326: the application must never terminate because the data directory cannot be written. The worst
/// acceptable outcome is a degraded state that still serves <c>/health</c>, the OpenAPI surface and the
/// Blazor pages, so an operator can see what happened and reach the documented recovery route.
/// <para>
/// These deliberately do not use throwing fakes for <c>IDatabaseInitializer</c>/<c>IAppVersionTracker</c>.
/// A hand-thrown exception would prove only that <c>Program.cs</c> tolerates whatever the fake throws;
/// pointing the real <c>SqliteConnectionFactory</c> at an unopenable path exercises the real initializer,
/// the real tracker and the real SQLite error code: the thing that actually failed live.
/// </para>
/// <para>
/// The sabotage techniques are the ones <c>scripts/testing/sqlite-storage-probe.csx</c> measured (2026-08-20):
/// a <em>directory</em> at the database path makes SQLite fail to open with <c>SQLITE_CANTOPEN</c> at the
/// same throw site as the live container, and a <em>file</em> named <c>keys</c> makes
/// <c>Directory.CreateDirectory</c> throw. Both are deterministic and behave identically on Windows and
/// Linux, which an ACL- or permission-based approach would not.
/// </para>
/// </summary>
[TestClass]
public class StartupResilienceTests
{
    private readonly List<TempDirectory> _temporaryDirectories = [];

    [TestCleanup]
    public void RemoveTemporaryDirectories()
    {
        foreach (TempDirectory directory in _temporaryDirectories)
            directory.Dispose();
    }

    [TestMethod]
    public void Startup_DataDirectoryNotWritable_EntersDegradedStateInsteadOfCrashing()
    {
        using WebApplicationFactory<Program> factory = FactoryWithUnopenableDatabase();

        using HttpClient client = factory.CreateClient();

        Assert.IsTrue(
            factory.Services.GetRequiredService<StartupPhaseState>().IsComplete,
            "startup never completed: the process died before it could reach a degraded state, which is "
            + "the failure #326 reports");
    }

    [TestMethod]
    public async Task Startup_DataDirectoryNotWritable_HealthReportsUnhealthyRatherThanBeingUnreachable()
    {
        using WebApplicationFactory<Program> factory = FactoryWithUnopenableDatabase();

        using HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(HealthRoute, TestContext.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("unhealthy", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "data directory", body, StringComparison.OrdinalIgnoreCase,
            "the stated reason must name the data directory and its remedy. The generic reason tells the "
            + "operator to run a database Reset, which also writes and therefore cannot work here");
    }

    [TestMethod]
    public async Task Startup_DataDirectoryNotWritable_OpenApiRemainsReachableForRecovery()
    {
        using WebApplicationFactory<Program> factory = FactoryWithUnopenableDatabase();

        using HttpClient client = factory.CreateClient();
        HttpResponseMessage openApi = await client.GetAsync("/openapi/v1.json", TestContext.CancellationToken);
        HttpResponseMessage admin = await client.GetAsync("/api/v1/admin/database/seed/preview", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, openApi.StatusCode, "the OpenAPI surface is the documented recovery route");
        string adminBody = await admin.Content.ReadAsStringAsync(TestContext.CancellationToken);
        Assert.IsFalse(
            adminBody.Contains("\"status\":\"unavailable\"", StringComparison.OrdinalIgnoreCase),
            "the admin surface was answered by the health gate rather than reaching its handler, so "
            + "POST /api/v1/admin/database/reset would be unreachable too");
    }

    /// <summary>
    /// Every Blazor route `DatabaseHealthGateMiddleware` exempts is, by construction, reachable exactly
    /// when the database is broken, so each one must render rather than 500. Covering only "/" would
    /// have missed that the same defect reaches several pages through shared components.
    /// </summary>
    [TestMethod]
    [DataRow("/")]
    [DataRow("/about")]
    [DataRow("/stats")]
    [DataRow("/notifications")]
    [DataRow("/rest-api")]
    public async Task Startup_DataDirectoryNotWritable_BlazorPageRendersDegradedUiRatherThan500(string route)
    {
        using WebApplicationFactory<Program> factory = FactoryWithUnopenableDatabase();

        using HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(route, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"{route} must render degraded UI, not 500");
    }

    /// <summary>
    /// #327: a database file that is not a database at all. Distinct from the unopenable-path case
    /// above: there SQLite cannot reach a file, here it reaches one and rejects its contents, which is
    /// what a half-written or externally-corrupted volume actually produces.
    /// </summary>
    [TestMethod]
    public void Startup_DatabaseFileCorrupt_EntersDegradedStateInsteadOfCrashing()
    {
        using WebApplicationFactory<Program> factory = FactoryWithCorruptDatabase();

        using HttpClient client = factory.CreateClient();

        Assert.IsTrue(
            factory.Services.GetRequiredService<StartupPhaseState>().IsComplete,
            "startup never completed: the process died rather than degrading, which is the outcome the "
            + "never-crash contract forbids");
    }

    [TestMethod]
    public async Task Startup_DatabaseFileCorrupt_HealthReportsUnhealthyRatherThanBeingUnreachable()
    {
        using WebApplicationFactory<Program> factory = FactoryWithCorruptDatabase();

        using HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(HealthRoute, TestContext.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("unhealthy", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// #350: a recorded schema version ahead of this build means the migrations this build never saw
    /// may have added, altered or removed anything, so the schema's shape is unknown and serving quote
    /// content from it is a foot gun. It degrades like every other startup fault in this class.
    /// <para>
    /// Replaces #327's <c>StaysHealthyAndSurfacesTheOvershoot</c>, which asserted the reverse contract
    /// #289 shipped. Removed rather than edited: its name stated that contract, and flipping the
    /// assertion in place would have left a name that lies about what it checks.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task Startup_SchemaVersionAheadOfApplication_DegradesRatherThanServing()
    {
        using WebApplicationFactory<Program> factory = FactoryWithASchemaVersionAheadOfThisBuild();

        using HttpClient client = factory.CreateClient();
        HttpResponseMessage quote = await client.GetAsync(RandomQuoteRoute, TestContext.CancellationToken);
        string body = await quote.Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.IsTrue(
            factory.Services.GetRequiredService<StartupPhaseState>().IsComplete,
            "startup never completed: degrading must never cost the process, which is what keeps "
            + "/health, the pages and the recovery routes answering");
        Assert.AreEqual(
            HttpStatusCode.ServiceUnavailable, quote.StatusCode,
            "quote content was served from a schema this build cannot vouch for");
        Assert.Contains("unavailable", body, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task Startup_SchemaVersionAheadOfApplication_HealthReasonNamesTheOvershoot()
    {
        using WebApplicationFactory<Program> factory = FactoryWithASchemaVersionAheadOfThisBuild();

        using HttpClient client = factory.CreateClient();
        HttpResponseMessage health = await client.GetAsync(HealthRoute, TestContext.CancellationToken);
        string body = await health.Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, health.StatusCode);
        Assert.Contains("unhealthy", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "schema version", body, StringComparison.OrdinalIgnoreCase,
            "the reason must name the fault. A generic reason sends the operator looking for a "
            + "different problem than the one they have");
        Assert.Contains(
            "ahead", body, StringComparison.OrdinalIgnoreCase,
            "which direction the version is wrong in is the whole fault: behind this build is a "
            + "pending migration, ahead of it is a schema of unknown shape");
    }

    /// <summary>
    /// #350: an overshoot is resolvable by a Reset or by restoring a backup taken before the version
    /// moved ahead. Naming only the Reset tells an operator who wants their data back that their only
    /// route is the one that discards it.
    /// </summary>
    [TestMethod]
    public async Task Startup_SchemaVersionAheadOfApplication_HealthReasonNamesBothRemedies()
    {
        using WebApplicationFactory<Program> factory = FactoryWithASchemaVersionAheadOfThisBuild();

        using HttpClient client = factory.CreateClient();
        HttpResponseMessage health = await client.GetAsync(HealthRoute, TestContext.CancellationToken);
        string body = await health.Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.Contains("reset", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "restore", body, StringComparison.OrdinalIgnoreCase,
            "the second remedy is missing, so the reason reads as though a Reset were the only way out");
    }

    /// <summary>
    /// Every Blazor route <c>DatabaseHealthGateMiddleware</c> exempts is, by construction, reachable
    /// exactly when the database is degraded, so each one must render rather than 500.
    /// </summary>
    [TestMethod]
    [DataRow("/")]
    [DataRow("/about")]
    [DataRow("/stats")]
    [DataRow("/notifications")]
    [DataRow("/rest-api")]
    [DataRow("/import-review")]
    public async Task Startup_SchemaVersionAheadOfApplication_BlazorPagesRenderDegradedUiRatherThan500(string route)
    {
        using WebApplicationFactory<Program> factory = FactoryWithASchemaVersionAheadOfThisBuild();

        using HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(route, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"{route} must render degraded UI, not 500");
    }

    [TestMethod]
    public async Task Startup_SchemaVersionAheadOfApplication_OpenApiRemainsReachableForRecovery()
    {
        using WebApplicationFactory<Program> factory = FactoryWithASchemaVersionAheadOfThisBuild();

        using HttpClient client = factory.CreateClient();
        HttpResponseMessage openApi = await client.GetAsync("/openapi/v1.json", TestContext.CancellationToken);
        HttpResponseMessage admin = await client.GetAsync("/api/v1/admin/database/seed/preview", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, openApi.StatusCode, "the OpenAPI surface is the documented recovery route");
        string adminBody = await admin.Content.ReadAsStringAsync(TestContext.CancellationToken);
        Assert.IsFalse(
            adminBody.Contains("\"status\":\"unavailable\"", StringComparison.OrdinalIgnoreCase),
            "the admin surface was answered by the health gate rather than reaching its handler, so "
            + "POST /api/v1/admin/database/reset would be unreachable too");
    }

    /// <summary>
    /// #350: the overshoot notification is the only record of the fault that outlives the restart, so
    /// it is written although the database is unhealthy. Read through <c>INotificationReader</c>
    /// rather than <c>GET /api/v1/notifications</c>, which the health gate answers while degraded:
    /// asserting through that route would assert the gate, not the notification.
    /// </summary>
    [TestMethod]
    public async Task Startup_SchemaVersionAheadOfApplication_SurfacesTheNotificationWhileDegraded()
    {
        using WebApplicationFactory<Program> factory = FactoryWithASchemaVersionAheadOfThisBuild();

        using HttpClient client = factory.CreateClient();

        IReadOnlyList<NotificationEntity> raised =
            await factory.Services.GetRequiredService<INotificationReader>()
                         .GetByMetadataKindAsync(NotificationMetadataKind.SchemaVersionOvershoot);

        Assert.IsNotEmpty(
            raised,
            "the overshoot was detected and degraded the application, but nothing was raised to say "
            + "why, so the notification explaining the degraded state is suppressed by it");
    }

    [TestMethod]
    public async Task Startup_KeysDirectoryCannotBeCreated_StartsDegradedInsteadOfCrashingBeforeKestrelBinds()
    {
        string dataDirectory = NewDataDirectory();
        // A file where the keys/ directory belongs. Directory.CreateDirectory then throws IOException,
        // deterministically, and identically on Windows and Linux. This runs at Program.cs:233, before
        // app.StartAsync(), so an unguarded throw kills the process before Kestrel binds: no wait page,
        // no /health, no OpenAPI at all.
        File.WriteAllText(Path.Combine(dataDirectory, DataPaths.DataProtectionFolder), "not a directory");

        using WebApplicationFactory<Program> factory = FactoryFor(dataDirectory);

        using HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(HealthRoute, TestContext.CancellationToken);

        Assert.AreEqual(
            HttpStatusCode.ServiceUnavailable, response.StatusCode,
            "the app must still be serving health, degraded, rather than having died before binding");
    }

    private const string HealthRoute = "/api/v1/health";
    private const string RandomQuoteRoute = "/api/v1/quotes/random";

    /// <summary>
    /// A database that really migrated, then had one version recorded past what this build knows.
    /// Reached that way deliberately: bumping the counter of an empty directory sets the same flag
    /// from a database that never migrated at all, which is a different state.
    /// </summary>
    private WebApplicationFactory<Program> FactoryWithASchemaVersionAheadOfThisBuild()
    {
        string dataDirectory = NewDataDirectory();

        using (WebApplicationFactory<Program> firstRun = FactoryFor(dataDirectory))
        {
            using HttpClient warmUp = firstRun.CreateClient();
        }

        RecordASchemaVersionAheadOfThisBuild(dataDirectory);

        return FactoryFor(dataDirectory);
    }

    private WebApplicationFactory<Program> FactoryWithCorruptDatabase()
    {
        string dataDirectory = NewDataDirectory();
        // Bytes that are not a SQLite file: the 16-byte header check fails and SQLite reports
        // SQLITE_NOTADB. Deliberately not a truncated real database: that reports SQLITE_CORRUPT from
        // a different code path, and producing one in-process would mean seeding a database first only
        // to chop it, which is slower and pins less. The container scenario covers truncation.
        File.WriteAllText(
            Path.Combine(dataDirectory, DataPaths.DatabaseFile),
            "this file is not a SQLite database");

        return FactoryFor(dataDirectory);
    }

    /// <summary>
    /// Records one version beyond whatever this build actually migrated to, rather than a literal:
    /// the suite's rule that no test asserts or depends on a specific migration number applies here
    /// too, and a literal would need editing every time a milestone adds a migration.
    /// </summary>
    private static void RecordASchemaVersionAheadOfThisBuild(string dataDirectory)
    {
        using IDbConnection connection =
            new SqliteConnectionFactory(Path.Combine(dataDirectory, DataPaths.DatabaseFile)).CreateConnection();

        int recorded = connection.ExecuteScalar<int>(
            "SELECT COALESCE(MAX(Version), 0) FROM System_ConsumerSchemaVersion;");

        connection.Execute(
            "INSERT INTO System_ConsumerSchemaVersion (Version, AppliedAt) VALUES (@v, @at);",
            new { v = recorded + 1, at = DateTimeOffset.UtcNow.ToString("O") });
    }

    private WebApplicationFactory<Program> FactoryWithUnopenableDatabase()
    {
        string dataDirectory = NewDataDirectory();
        // A directory where the database file belongs: SQLite fails to open it with SQLITE_CANTOPEN at
        // DatabaseInitializer.EnableWal: the same throw site, and the same propagation path through
        // AppVersionTracker.GetLastActiveAsync, as the live read-only-mount failure.
        Directory.CreateDirectory(Path.Combine(dataDirectory, DataPaths.DatabaseFile));

        return FactoryFor(dataDirectory);
    }

    private static WebApplicationFactory<Program> FactoryFor(string dataDirectory) =>
        new QuotinatorWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Quotinator:DataDir", dataDirectory);
        });

    private string NewDataDirectory()
    {
        TempDirectory directory = new("quotinator_326_resilience_");
        _temporaryDirectories.Add(directory);
        return directory.Path;
    }

    public TestContext TestContext { get; set; }
}

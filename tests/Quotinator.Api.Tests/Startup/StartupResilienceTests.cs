using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Startup;
using Quotinator.Data.Paths;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// #326 — the application must never terminate because the data directory cannot be written. The worst
/// acceptable outcome is a degraded state that still serves <c>/health</c>, the OpenAPI surface and the
/// Blazor pages, so an operator can see what happened and reach the documented recovery route.
/// <para>
/// These deliberately do not use throwing fakes for <c>IDatabaseInitializer</c>/<c>IAppVersionTracker</c>.
/// A hand-thrown exception would prove only that <c>Program.cs</c> tolerates whatever the fake throws;
/// pointing the real <c>SqliteConnectionFactory</c> at an unopenable path exercises the real initializer,
/// the real tracker and the real SQLite error code — the thing that actually failed live.
/// </para>
/// <para>
/// The sabotage techniques are the ones <c>scripts/sqlite-storage-probe.csx</c> measured (2026-08-20):
/// a <em>directory</em> at the database path makes SQLite fail to open with <c>SQLITE_CANTOPEN</c> at the
/// same throw site as the live container, and a <em>file</em> named <c>keys</c> makes
/// <c>Directory.CreateDirectory</c> throw. Both are deterministic and behave identically on Windows and
/// Linux, which an ACL- or permission-based approach would not.
/// </para>
/// </summary>
[TestClass]
public class StartupResilienceTests
{
    private readonly List<string> _temporaryDirectories = [];

    [TestCleanup]
    public void RemoveTemporaryDirectories()
    {
        foreach (string directory in _temporaryDirectories)
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { /* a still-open SQLite handle is not this test's concern */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    [TestMethod]
    public void Startup_DataDirectoryNotWritable_EntersDegradedStateInsteadOfCrashing()
    {
        using WebApplicationFactory<Program> factory = FactoryWithUnopenableDatabase();

        using HttpClient client = factory.CreateClient();

        Assert.IsTrue(
            factory.Services.GetRequiredService<StartupPhaseState>().IsComplete,
            "startup never completed — the process died before it could reach a degraded state, which is "
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
        StringAssert.Contains(body, "unhealthy", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(
            body, "data directory", StringComparison.OrdinalIgnoreCase,
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
    /// when the database is broken — so each one must render rather than 500. Covering only "/" would
    /// have missed that the same defect reaches several pages through shared components.
    /// </summary>
    [DataTestMethod]
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

    [TestMethod]
    public async Task Startup_KeysDirectoryCannotBeCreated_StartsDegradedInsteadOfCrashingBeforeKestrelBinds()
    {
        string dataDirectory = NewDataDirectory();
        // A file where the keys/ directory belongs. Directory.CreateDirectory then throws IOException —
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

    private WebApplicationFactory<Program> FactoryWithUnopenableDatabase()
    {
        string dataDirectory = NewDataDirectory();
        // A directory where the database file belongs: SQLite fails to open it with SQLITE_CANTOPEN at
        // DatabaseInitializer.EnableWal — the same throw site, and the same propagation path through
        // AppVersionTracker.GetLastActiveAsync, as the live read-only-mount failure.
        Directory.CreateDirectory(Path.Combine(dataDirectory, DataPaths.DatabaseFile));

        return FactoryFor(dataDirectory);
    }

    private static WebApplicationFactory<Program> FactoryFor(string dataDirectory) =>
        new QuotinatorWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.UseSetting("Quotinator:DataDir", dataDirectory));

    private string NewDataDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "quotinator-326-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _temporaryDirectories.Add(directory);
        return directory;
    }

    public TestContext TestContext { get; set; }
}

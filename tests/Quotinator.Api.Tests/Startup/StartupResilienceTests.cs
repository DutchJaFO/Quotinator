using System.Data;
using System.Net;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Api.Services;
using Quotinator.Api.Startup;
using Quotinator.Data.Connections;
using Quotinator.Data.Database.Checks;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Import;
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
    private readonly List<string> _deniedDirectories = [];

    [TestCleanup]
    public void RemoveTemporaryDirectories()
    {
        foreach (string keys in _deniedDirectories)
            LiftWriteDenial(keys);

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
    /// <c>/version</c> does not call itself ready on a database that could not be opened.
    /// </summary>
    /// <remarks>
    /// #419 gated this endpoint while startup is still running, for a reason that applies just as much
    /// once startup has finished badly: a caller could otherwise reach for it as a readiness endpoint
    /// in <c>/health</c>'s place. It stays exempt from
    /// <see cref="Api.Middleware.DatabaseHealthGateMiddleware"/> per #263, so the operator can still see
    /// which build is running; being reachable is not the same as being ready, and <c>/health</c> is
    /// reachable while unhealthy for exactly the same reason. Found by a T2 run against a corrupt
    /// database, 2026-10-21, which read <c>200</c> with <c>"status":"ready"</c>. See #327.
    /// </remarks>
    [TestMethod]
    public async Task Startup_DatabaseFileCorrupt_VersionDoesNotReportReady()
    {
        using WebApplicationFactory<Program> factory = FactoryWithCorruptDatabase();

        using HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(VersionRoute, TestContext.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.DoesNotContain("\"status\":\"ready\"", body, StringComparison.OrdinalIgnoreCase,
            "the application reported itself ready while /health reported the database unopenable, so a "
            + "caller polling this endpoint would act on a readiness it does not have");
    }

    /// <summary>
    /// <c>/version</c> serves no database counts while the database could not be read.
    /// </summary>
    /// <remarks>
    /// The sharper half, and the one a caller cannot defend itself against: every count read as
    /// <c>0</c> on a database that failed to open, which is indistinguishable from a genuinely empty
    /// one. Developer direction, 2026-10-21: with health reporting a problem, no endpoint that queries
    /// the database is expected to return sane results. See #327, and the suite index's
    /// <em>A degraded application is not expected to answer from its database</em>.
    /// </remarks>
    [TestMethod]
    public async Task Startup_DatabaseFileCorrupt_VersionServesNoDatabaseCounts()
    {
        using WebApplicationFactory<Program> factory = FactoryWithCorruptDatabase();

        using HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(VersionRoute, TestContext.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.DoesNotContain("\"database\"", body, StringComparison.OrdinalIgnoreCase,
            "counts read off a database that could not be opened are not counts, and a caller has no "
            + "way to tell them from a genuinely empty database");
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
        using WebApplicationFactory<Program> factory = FactoryWithKeysDirectoryBlocked();

        using HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(HealthRoute, TestContext.CancellationToken);

        Assert.AreEqual(
            HttpStatusCode.ServiceUnavailable, response.StatusCode,
            "the app must still be serving health, degraded, rather than having died before binding");
    }

    [TestMethod]
    public void Startup_KeysDirectoryCannotBeCreated_EntersDegradedStateInsteadOfCrashing()
    {
        using WebApplicationFactory<Program> factory = FactoryWithKeysDirectoryBlocked();

        using HttpClient client = factory.CreateClient();

        Assert.IsTrue(
            factory.Services.GetRequiredService<StartupPhaseState>().IsComplete,
            "startup never completed: the process died rather than degrading");
    }

    /// <summary>
    /// #327: a <c>keys/</c> directory that exists but cannot be written. <c>CreateDirectory</c> succeeds
    /// on it, so the failure only surfaces at the first key write, on a page request, while the
    /// application reports itself healthy: it did not know it was degraded.
    /// </summary>
    [TestMethod]
    public async Task Startup_KeysDirectoryNotWritable_HealthReportsUnhealthyRatherThanHealthy()
    {
        using WebApplicationFactory<Program> factory = FactoryWithKeysDirectoryNotWritable();

        using HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(HealthRoute, TestContext.CancellationToken);

        Assert.AreEqual(
            HttpStatusCode.ServiceUnavailable, response.StatusCode,
            "the key ring cannot be written, so the pages fail on every request; reporting healthy hides it");
    }

    [TestMethod]
    public void Startup_KeysDirectoryNotWritable_EntersDegradedStateInsteadOfCrashing()
    {
        using WebApplicationFactory<Program> factory = FactoryWithKeysDirectoryNotWritable();

        using HttpClient client = factory.CreateClient();

        Assert.IsTrue(
            factory.Services.GetRequiredService<StartupPhaseState>().IsComplete,
            "startup never completed: the process died rather than degrading");
    }

    /// <summary>
    /// #327: the data directory itself cannot be created, the earliest degraded path there is. It fails
    /// before Kestrel binds, so an unguarded throw would leave no wait page, no /health and no OpenAPI.
    /// </summary>
    [TestMethod]
    public void Startup_DataDirectoryCannotBeCreated_EntersDegradedStateInsteadOfCrashing()
    {
        using WebApplicationFactory<Program> factory = FactoryWithDataDirectoryPathOccupiedByAFile();

        using HttpClient client = factory.CreateClient();

        Assert.IsTrue(
            factory.Services.GetRequiredService<StartupPhaseState>().IsComplete,
            "startup never completed: the process died rather than degrading");
    }

    [TestMethod]
    public async Task Startup_DataDirectoryCannotBeCreated_HealthReportsUnhealthyRatherThanBeingUnreachable()
    {
        using WebApplicationFactory<Program> factory = FactoryWithDataDirectoryPathOccupiedByAFile();

        using HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(HealthRoute, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [TestMethod]
    public async Task Startup_DataDirectoryCannotBeCreated_HealthReasonNamesTheDataDirectory()
    {
        using WebApplicationFactory<Program> factory = FactoryWithDataDirectoryPathOccupiedByAFile();

        using HttpClient client = factory.CreateClient();
        string body = await (await client.GetAsync(HealthRoute, TestContext.CancellationToken))
            .Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.Contains("Restore write access to the data directory", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// #327: the reason must carry deleting the file as a remedy, not only the Reset. Since #457 it is the
    /// unsound-file reason, which leads with the Reset, since #482 made the Reset recover a file that is not
    /// a database, then restoring a backup, then deleting the file (decision 2).
    /// </summary>
    [TestMethod]
    public async Task Startup_DatabaseFileCorrupt_HealthReasonNamesARemedyThatCanWork()
    {
        using WebApplicationFactory<Program> factory = FactoryWithCorruptDatabase();

        using HttpClient client = factory.CreateClient();
        string body = await (await client.GetAsync(HealthRoute, TestContext.CancellationToken))
            .Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.Contains("deleting the database file", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// #327: a real database cut in half. SQLite reaches the file and finds it damaged, a different fault
    /// from a file that was never a database, on a different code path.
    /// </summary>
    [TestMethod]
    public void Startup_DatabaseFileTruncated_EntersDegradedStateInsteadOfCrashing()
    {
        using WebApplicationFactory<Program> factory = FactoryWithTruncatedDatabase();

        using HttpClient client = factory.CreateClient();

        Assert.IsTrue(
            factory.Services.GetRequiredService<StartupPhaseState>().IsComplete,
            "startup never completed: the process died rather than degrading");
    }

    [TestMethod]
    public async Task Startup_DatabaseFileTruncated_HealthReportsUnhealthyRatherThanBeingUnreachable()
    {
        using WebApplicationFactory<Program> factory = FactoryWithTruncatedDatabase();

        using HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(HealthRoute, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [TestMethod]
    public async Task Startup_DatabaseFileTruncated_HealthReasonNamesARemedyThatCanWork()
    {
        using WebApplicationFactory<Program> factory = FactoryWithTruncatedDatabase();

        using HttpClient client = factory.CreateClient();
        string body = await (await client.GetAsync(HealthRoute, TestContext.CancellationToken))
            .Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.Contains("deleting the database file", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// #457: a file that is not a database fails the check's Format layer, and the reason says what is wrong with the
    /// file rather than diagnosing a schema-version mismatch.
    /// </summary>
    [TestMethod]
    public void Startup_DatabaseFileCorrupt_HealthReasonNamesTheFault()
    {
        using WebApplicationFactory<Program> factory = FactoryWithCorruptDatabase();

        using HttpClient client = factory.CreateClient();
        DatabaseHealthState health = factory.Services.GetRequiredService<DatabaseHealthState>();

        Assert.AreEqual(DatabaseCheckLayer.Format, health.StartupCheck?.FirstFailure?.Layer, "precondition: the file fails at Format");
        Assert.DoesNotContain("schema version", health.FailureReason!, StringComparison.OrdinalIgnoreCase);
        Assert.AreEqual(DatabaseUnsoundGuidance.Reason(health.StartupCheck!.FirstFailure!, null), health.FailureReason);
    }

    /// <summary>
    /// #457: a real database cut in half fails the Pages layer (measured 2026-11-18), and its reason names that fault.
    /// </summary>
    [TestMethod]
    public void Startup_DatabaseFileTruncated_HealthReasonNamesTheFault()
    {
        using WebApplicationFactory<Program> factory = FactoryWithTruncatedDatabase();

        using HttpClient client = factory.CreateClient();
        DatabaseHealthState health = factory.Services.GetRequiredService<DatabaseHealthState>();

        Assert.AreEqual(DatabaseCheckLayer.Pages, health.StartupCheck?.FirstFailure?.Layer, "precondition: the file fails at Pages");
        Assert.DoesNotContain("schema version", health.FailureReason!, StringComparison.OrdinalIgnoreCase);
        Assert.AreEqual(DatabaseUnsoundGuidance.Reason(health.StartupCheck!.FirstFailure!, null), health.FailureReason);
    }

    /// <summary>
    /// #457, decision 3: when the reserved folder cannot be created, a Reset would be refused, so the reason does not
    /// offer it and names what stops it.
    /// </summary>
    [TestMethod]
    public async Task Startup_DatabaseFileCorrupt_SetAsideFolderCannotBeCreated_HealthReasonDoesNotOfferTheReset()
    {
        using WebApplicationFactory<Program> factory = FactoryWithCorruptDatabase(blockTheSetAsideFolder: true);

        using HttpClient client = factory.CreateClient();
        string body = await (await client.GetAsync(HealthRoute, TestContext.CancellationToken))
            .Content.ReadAsStringAsync(TestContext.CancellationToken);
        string reason = JsonDocument.Parse(body).RootElement.GetProperty("reason").GetString()!;

        Assert.DoesNotContain(BackupObstacleGuidance.ResetSetsTheFileAside, reason, StringComparison.Ordinal);
        Assert.Contains(ResetObstacleGuidance.FreeTheSetAsideFolder, reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// #457, decision E: a database the check cannot open, held by another handle here, may be intact, so the reason
    /// leads with restoring access rather than with a Reset that would set it aside. The lock is the fixture because it
    /// fails the open the same way on Windows (a sharing violation) and Linux (.NET's advisory lock), where a directory
    /// at the path is not a file at all and a permission is ignored by a root test runner.
    /// </summary>
    [TestMethod]
    public void Startup_DatabaseFileCannotBeOpened_HealthReasonRestoresAccessFirst()
    {
        string dataDirectory = DataDirectoryWithAMigratedDatabase();
        using FileStream held = new(Path.Combine(dataDirectory, DataPaths.DatabaseFile), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using WebApplicationFactory<Program> factory = FactoryFor(dataDirectory);

        using HttpClient client = factory.CreateClient();
        DatabaseHealthState health = factory.Services.GetRequiredService<DatabaseHealthState>();

        Assert.AreEqual(DatabaseStorageFault.FileUnreadable, health.StartupCheck?.FirstFailure?.StorageFault, "precondition: the check could not open the file");
        Assert.AreEqual(DatabaseUnsoundGuidance.Reason(health.StartupCheck!.FirstFailure!, null), health.FailureReason);
        Assert.StartsWith(DatabaseUnsoundGuidance.Cause(health.StartupCheck.FirstFailure!), health.FailureReason!);
    }

    /// <summary>
    /// #327: a database whose schema is broken, a domain table missing from a database that otherwise
    /// migrated cleanly. Here a Reset can succeed, since the file opens and the schema can be rebuilt.
    /// </summary>
    [TestMethod]
    public void Startup_DomainTableMissing_EntersDegradedStateInsteadOfCrashing()
    {
        using WebApplicationFactory<Program> factory = FactoryWithAMissingDomainTable();

        using HttpClient client = factory.CreateClient();

        Assert.IsTrue(
            factory.Services.GetRequiredService<StartupPhaseState>().IsComplete,
            "startup never completed: the process died rather than degrading");
    }

    [TestMethod]
    public async Task Startup_DomainTableMissing_HealthReportsUnhealthyRatherThanBeingUnreachable()
    {
        using WebApplicationFactory<Program> factory = FactoryWithAMissingDomainTable();

        using HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(HealthRoute, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [TestMethod]
    public async Task Startup_DomainTableMissing_HealthReasonNamesTheReset()
    {
        using WebApplicationFactory<Program> factory = FactoryWithAMissingDomainTable();

        using HttpClient client = factory.CreateClient();
        string body = await (await client.GetAsync(HealthRoute, TestContext.CancellationToken))
            .Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.Contains("database Reset", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// #305: a table missing behind a current schema version, the live incident's table, is found by the
    /// integrity check at startup and degrades rather than reporting healthy.
    /// <see cref="Startup_RestartOverAnIntactDatabase_ReportsHealthy"/> is the positive control.
    /// </summary>
    [TestMethod]
    public async Task Startup_SchemaDifference_HealthReportsUnhealthy()
    {
        using WebApplicationFactory<Program> factory = FactoryWithASchemaDifference("DROP TABLE System_Notification;");

        using HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(HealthRoute, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [TestMethod]
    [DataRow("DROP TABLE System_Notification;", "System_Notification")]
    [DataRow("ALTER TABLE System_Notification DROP COLUMN DateDeleted;", "System_Notification.DateDeleted")]
    public async Task Startup_SchemaDifference_HealthReasonNamesTheObject(string sabotage, string objectName)
    {
        using WebApplicationFactory<Program> factory = FactoryWithASchemaDifference(sabotage);

        using HttpClient client = factory.CreateClient();
        string body = await (await client.GetAsync(HealthRoute, TestContext.CancellationToken))
            .Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.Contains(objectName, body, StringComparison.Ordinal);
        Assert.Contains("database Reset", body, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task Startup_SchemaDifference_QuoteEndpointsAreGated()
    {
        using WebApplicationFactory<Program> factory = FactoryWithASchemaDifference("DROP TABLE System_Notification;");

        using HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(RandomQuoteRoute, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode,
            "quote content was served from a schema known not to match its version");
    }

    [TestMethod]
    [DataRow("/")]
    [DataRow("/notifications")]
    public async Task Startup_SchemaDifference_BlazorPageRendersDegradedUi(string route)
    {
        using WebApplicationFactory<Program> factory = FactoryWithASchemaDifference("DROP TABLE System_Notification;");

        using HttpClient client = factory.CreateClient();
        HttpResponseMessage health = await client.GetAsync(HealthRoute, TestContext.CancellationToken);
        HttpResponseMessage response = await client.GetAsync(route, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, health.StatusCode, "precondition: the application degraded");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"{route} must render degraded UI, not 500");
    }

    /// <summary>
    /// #305 (R2): a changelog database whose schema does not match is not loaded into; the changelog falls
    /// back to its JSON files, and main-database health is unaffected. The sabotage is a dropped index,
    /// which the import would tolerate, so only the check can be what stops it: a dropped table makes the
    /// import fail on its own and would pass without the check.
    /// </summary>
    [TestMethod]
    public async Task Startup_ChangelogSchemaDifference_FallsBackToTheJsonFilesAndStaysHealthy()
    {
        string dataDirectory = DataDirectoryWithAMigratedDatabase();
        using (IDbConnection connection =
            new SqliteConnectionFactory(Path.Combine(dataDirectory, DataPaths.ChangelogDatabaseFile)).CreateConnection())
        {
            connection.Execute("DROP INDEX IX_Changelog_Line_ChangelogEntryId;");
        }
        SqliteConnection.ClearAllPools();

        using WebApplicationFactory<Program> factory = FactoryFor(dataDirectory);
        using HttpClient client = factory.CreateClient();

        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync(HealthRoute, TestContext.CancellationToken)).StatusCode,
            "a changelog database that does not match must not affect main-database health");
        Assert.AreEqual(ChangelogImportOutcome.Failed,
            await factory.Services.GetRequiredService<IChangelogImportReadiness>().WaitAsync(TestContext.CancellationToken),
            "the import must not run into a schema known not to match, so the reader serves the JSON files");
    }

    /// <summary>#305: the remedy the reason names works: a Reset rebuilds the schema and health returns.</summary>
    [TestMethod]
    public async Task Startup_SchemaDifference_ResetRestoresHealth()
    {
        using WebApplicationFactory<Program> factory = FactoryWithASchemaDifference("DROP TABLE System_Notification;", AdminKey);

        using HttpClient client = factory.CreateClient();
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable,
            (await client.GetAsync(HealthRoute, TestContext.CancellationToken)).StatusCode, "precondition: the application degraded");

        using HttpRequestMessage reset = new(HttpMethod.Post, "/api/v1/admin/database/reset");
        reset.Headers.Add("X-Api-Key", AdminKey);
        HttpResponseMessage resetResponse = await client.SendAsync(reset, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, resetResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync(HealthRoute, TestContext.CancellationToken)).StatusCode);
    }

    /// <summary>#482: the Reset the degraded application offers rebuilds a file SQLite cannot read.</summary>
    [TestMethod]
    public async Task Startup_DatabaseFileCorrupt_ResetRecoversToHealthy()
    {
        using WebApplicationFactory<Program> factory = FactoryWithCorruptDatabase(AdminKey);
        using HttpClient client = factory.CreateClient();
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable,
            (await client.GetAsync(HealthRoute, TestContext.CancellationToken)).StatusCode, "precondition: the application degraded");

        HttpResponseMessage reset = await PostResetAsync(client);

        Assert.AreEqual(HttpStatusCode.OK, reset.StatusCode, await reset.Content.ReadAsStringAsync(TestContext.CancellationToken));
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync(HealthRoute, TestContext.CancellationToken)).StatusCode);
    }

    /// <summary>
    /// #482: the notification is written after the new database is in place, names the set-aside file, and
    /// carries its text in every language rather than English only.
    /// </summary>
    /// <remarks>
    /// The Reset's own status is deliberately not asserted first. Before #482 the Reset refused, and a test
    /// stopping on that refusal would be red on a precondition rather than on the notification it claims,
    /// which the red-first rule does not count (#423). The notification is asserted first, so the test is
    /// red on its own claim wherever no set-aside notification exists.
    /// </remarks>
    [TestMethod]
    public async Task Startup_DatabaseFileCorrupt_ResetLeavesANotificationNamingTheSetAsideFile()
    {
        using WebApplicationFactory<Program> factory = FactoryWithCorruptDatabase(AdminKey);
        using HttpClient client = factory.CreateClient();
        await PostResetAsync(client);

        using HttpRequestMessage request = new(HttpMethod.Get, "/api/v1/notifications");
        request.Headers.Add("Accept-Language", "nl");
        using HttpResponseMessage listing = await client.SendAsync(request, TestContext.CancellationToken);
        using JsonDocument doc = JsonDocument.Parse(await listing.Content.ReadAsStringAsync(TestContext.CancellationToken));

        // A degraded application answers this route with a problem document, which carries no list: no
        // notification can be read from it, which is the absence this test reports.
        IEnumerable<JsonElement> items = doc.RootElement.TryGetProperty("items", out JsonElement list) ? list.EnumerateArray() : [];
        JsonElement notification = Assert.ContainsSingle(
            items.Where(n => string.Equals(n.GetProperty("metadataKind").GetString(), "UnreadableDatabaseSetAside", StringComparison.OrdinalIgnoreCase)),
            $"no set-aside notification was written (GET /api/v1/notifications answered {(int)listing.StatusCode})");
        string setAside = Path.GetFileName(Assert.ContainsSingle(SetAsideMainFiles(factory), "the notification exists, but no file was set aside"));
        Assert.Contains(setAside, notification.GetProperty("body").GetString()!, StringComparison.Ordinal);
        Assert.IsTrue(notification.GetProperty("isTranslated").GetBoolean(), "the notification's text exists only in English");
    }

    /// <summary>
    /// #490: the repair action sets the file aside, and both notifications it leaves, the set-aside notice and
    /// the Reset's own result, say why in their text: the layer of the database check that showed the file
    /// was not a sound copy, its status and its finding, as the check itself reports them.
    /// </summary>
    [TestMethod]
    public async Task Startup_DatabaseFileCorrupt_RepairActionNotificationsSayWhichLayerShowedIt()
    {
        using WebApplicationFactory<Program> factory = FactoryWithCorruptDatabase(AdminKey);
        using HttpClient client = factory.CreateClient();
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable,
            (await client.GetAsync(HealthRoute, TestContext.CancellationToken)).StatusCode, "precondition: the application degraded");

        using IServiceScope scope = factory.Services.CreateScope();
        DatabaseCheckResult check = await scope.ServiceProvider.GetRequiredService<IDatabaseCheck>().CheckAsync(DatabaseCheckDepth.Full);
        DatabaseCheckLayerResult expected = check.Layers.First(l => l.Status is DatabaseCheckLayerStatus.Failed or DatabaseCheckLayerStatus.Inconclusive);
        await scope.ServiceProvider.GetRequiredService<INotificationActionExecutor>()
            .ExecuteAsync(NotificationDismissTrigger.DatabaseReset);

        using HttpRequestMessage request = new(HttpMethod.Get, "/api/v1/notifications");
        request.Headers.Add("Accept-Language", "nl");
        using HttpResponseMessage listing = await client.SendAsync(request, TestContext.CancellationToken);
        using JsonDocument doc = JsonDocument.Parse(await listing.Content.ReadAsStringAsync(TestContext.CancellationToken));
        IEnumerable<JsonElement> items = doc.RootElement.TryGetProperty("items", out JsonElement list) ? list.EnumerateArray() : [];

        foreach (string kind in (string[])["UnreadableDatabaseSetAside", "DatabaseActionResult"])
        {
            string body = Assert.ContainsSingle(
                items.Where(n => string.Equals(n.GetProperty("metadataKind").GetString(), kind, StringComparison.OrdinalIgnoreCase)),
                $"no {kind} notification was written (GET /api/v1/notifications answered {(int)listing.StatusCode})")
                .GetProperty("body").GetString()!;
            Assert.Contains(expected.Layer.ToString(), body, StringComparison.Ordinal, $"the {kind} notification does not name the layer");
            Assert.Contains(expected.Status.ToString(), body, StringComparison.Ordinal, $"the {kind} notification does not name the status");
            Assert.Contains(expected.Finding!, body, StringComparison.Ordinal, $"the {kind} notification does not give the finding");
        }
    }

    /// <summary>
    /// #490: the Reset's own response says why it set the file aside, as its notifications do: the check
    /// layer that showed the file was not a sound copy, its status and its finding, as the check reports them.
    /// </summary>
    [TestMethod]
    public async Task Startup_DatabaseFileCorrupt_ResetResponseSaysWhichLayerShowedIt()
    {
        using WebApplicationFactory<Program> factory = FactoryWithCorruptDatabase(AdminKey);
        using HttpClient client = factory.CreateClient();
        DatabaseCheckLayerResult expected = await FirstLayerThatDidNotPassAsync(factory);

        HttpResponseMessage reset = await PostResetAsync(client);

        using JsonDocument body = JsonDocument.Parse(await reset.Content.ReadAsStringAsync(TestContext.CancellationToken));
        Assert.AreEqual(HttpStatusCode.OK, reset.StatusCode, "precondition: the Reset ran: " + body.RootElement);
        JsonElement backup = body.RootElement.GetProperty("backup");
        Assert.AreEqual("SetAside", backup.GetProperty("outcome").GetString(), "precondition: the file was set aside");
        AssertNamesTheLayer(backup, expected);
    }

    /// <summary>
    /// #490: a Reset refused because the file could not be set aside says why the file needed setting aside,
    /// as well as the obstacle that stopped the move.
    /// </summary>
    [TestMethod]
    public async Task Startup_DatabaseFileCorrupt_ResetRefusalSaysWhichLayerShowedIt()
    {
        using WebApplicationFactory<Program> factory = FactoryWithCorruptDatabase(AdminKey, blockTheSetAsideFolder: true);
        using HttpClient client = factory.CreateClient();
        DatabaseCheckLayerResult expected = await FirstLayerThatDidNotPassAsync(factory);

        HttpResponseMessage reset = await PostResetAsync(client);

        using JsonDocument body = JsonDocument.Parse(await reset.Content.ReadAsStringAsync(TestContext.CancellationToken));
        Assert.AreEqual(HttpStatusCode.Conflict, reset.StatusCode, "precondition: the Reset refused: " + body.RootElement);
        AssertNamesTheLayer(body.RootElement, expected);
    }

    /// <summary>The first layer of a full check of the main database that did not pass: the one a Reset sets the file aside for.</summary>
    private static async Task<DatabaseCheckLayerResult> FirstLayerThatDidNotPassAsync(WebApplicationFactory<Program> factory)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        DatabaseCheckResult check = await scope.ServiceProvider.GetRequiredService<IDatabaseCheck>().CheckAsync(DatabaseCheckDepth.Full);
        return check.Layers.First(l => l.Status is DatabaseCheckLayerStatus.Failed or DatabaseCheckLayerStatus.Inconclusive);
    }

    private static void AssertNamesTheLayer(JsonElement element, DatabaseCheckLayerResult expected)
    {
        Assert.IsTrue(element.TryGetProperty("checkLayer", out JsonElement layer), "no checkLayer was reported: " + element);
        Assert.AreEqual(expected.Layer.ToString(), layer.GetString());
        Assert.AreEqual(expected.Status.ToString(), element.GetProperty("checkStatus").GetString());
        Assert.AreEqual(expected.Finding, element.GetProperty("checkFinding").GetString());
    }

    /// <summary>
    /// #482: the degraded page's repair action runs the same Reset through the executor, so it recovers an
    /// unreadable file too, rather than reporting the refusal it used to.
    /// </summary>
    [TestMethod]
    public async Task Startup_DatabaseFileCorrupt_RepairActionRecoversToHealthy()
    {
        using WebApplicationFactory<Program> factory = FactoryWithCorruptDatabase(AdminKey);
        using HttpClient client = factory.CreateClient();
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable,
            (await client.GetAsync(HealthRoute, TestContext.CancellationToken)).StatusCode, "precondition: the application degraded");

        using IServiceScope scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<INotificationActionExecutor>()
            .ExecuteAsync(NotificationDismissTrigger.DatabaseReset);

        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync(HealthRoute, TestContext.CancellationToken)).StatusCode);
    }

    /// <summary>
    /// #482: a file occupying the reserved folder's name means the unreadable file has nowhere to go. The
    /// Reset refuses, names that obstacle rather than a backup one, and leaves the file where it was.
    /// </summary>
    [TestMethod]
    public async Task Startup_DatabaseFileCorrupt_ResetWhenTheSetAsideFolderCannotBeCreated_Returns409NamingTheObstacle()
    {
        using WebApplicationFactory<Program> factory = FactoryWithCorruptDatabase(AdminKey, blockTheSetAsideFolder: true);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage reset = await PostResetAsync(client);

        Assert.AreEqual(HttpStatusCode.Conflict, reset.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await reset.Content.ReadAsStringAsync(TestContext.CancellationToken));
        Assert.IsTrue(body.RootElement.TryGetProperty("resetObstacle", out JsonElement obstacle), "the refusal names no reset obstacle: " + body.RootElement);
        Assert.AreEqual("SetAsideFolderNotWritable", obstacle.GetString());
        // Shared read: the running application may hold the file open, which is not what is being asserted.
        using FileStream file = new(Path.Combine(DataDirectoryOf(factory), DataPaths.DatabaseFile), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(file);
        Assert.AreEqual(NotADatabase, await reader.ReadToEndAsync(TestContext.CancellationToken), "the unreadable file was not left in place");
    }

    /// <summary>
    /// #327: the positive control for every scenario that sabotages an empty data directory. Without
    /// it, each degraded assertion above would stay green against a build that degraded on everything.
    /// </summary>
    [TestMethod]
    public async Task Startup_FreshDataDirectory_ReportsHealthy()
    {
        using WebApplicationFactory<Program> factory = FactoryFor(NewDataDirectory());

        using HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(HealthRoute, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// #327: the positive control for every scenario that sabotages a database a first run created, the
    /// truncated file, the missing table and the version recorded ahead: the same two runs, unsabotaged.
    /// </summary>
    [TestMethod]
    public async Task Startup_RestartOverAnIntactDatabase_ReportsHealthy()
    {
        using WebApplicationFactory<Program> factory = FactoryOverAnIntactDatabase();

        using HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(HealthRoute, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// #490, step 10: with <c>Quotinator:DatabaseStartupCheck</c> at <c>Fast</c>, startup skips Indexes and
    /// References, so a database whose only fault is an index that disagrees with its table starts healthy. The
    /// check it kept says it ran at <c>Fast</c> and skipped those layers, rather than claiming they passed.
    /// </summary>
    [TestMethod]
    public void Startup_FastSetting_SkipsIndexesAndStaysHealthyOverABrokenIndex()
    {
        using WebApplicationFactory<Program> factory = FactoryWithAnIndexThatDisagreesWithItsTable(("Quotinator:DatabaseStartupCheck", "fast"));

        using HttpClient client = factory.CreateClient();
        DatabaseHealthState health = factory.Services.GetRequiredService<DatabaseHealthState>();

        Assert.IsTrue(health.IsHealthy, health.FailureReason);
        Assert.IsNotNull(health.StartupCheck, "Startup kept no check.");
        Assert.AreEqual(DatabaseCheckDepth.Fast, health.StartupCheck.Depth, "Startup did not check at the configured depth.");
        Assert.AreEqual(DatabaseCheckLayerStatus.Skipped, health.StartupCheck.Layers.Single(l => l.Layer == DatabaseCheckLayer.Indexes).Status);
        Assert.AreEqual(DatabaseCheckLayerStatus.Skipped, health.StartupCheck.Layers.Single(l => l.Layer == DatabaseCheckLayer.References).Status);
    }

    /// <summary>
    /// #490, row 17's positive control: a restart over an intact database is healthy, and keeps the check it
    /// ran with every layer passed.
    /// </summary>
    [TestMethod]
    public void Startup_RestartOverAnIntactDatabase_KeepsAPassedStartupCheck()
    {
        using WebApplicationFactory<Program> factory = FactoryOverAnIntactDatabase();

        using HttpClient client = factory.CreateClient();
        DatabaseHealthState health = factory.Services.GetRequiredService<DatabaseHealthState>();

        Assert.IsTrue(health.IsHealthy, health.FailureReason);
        Assert.IsNotNull(health.StartupCheck, "Startup kept no check.");
        Assert.IsTrue(health.StartupCheck.IsSound, $"First failure: {health.StartupCheck.FirstFailure?.Layer}");
        Assert.AreEqual(DatabaseCheckDepth.Full, health.StartupCheck.Depth);
    }

    /// <summary>
    /// #490, row 17: the Storage layer finds an unwritable data directory before the database is opened, not
    /// by opening it. The database and both its sidecars already exist and are writable, so SQLite itself
    /// would open the database without complaint and nothing else would notice; only the check's directory
    /// probe can. A Reset would then fail to write, which is why #326's reason says it cannot help.
    /// </summary>
    [TestMethod]
    public void Startup_DataDirectoryNotWritable_DetectedBeforeTheDatabaseIsOpened()
    {
        using WebApplicationFactory<Program> factory = FactoryWithExistingSidecarsInAnUnwritableDataDirectory();

        using HttpClient client = factory.CreateClient();
        DatabaseHealthState health = factory.Services.GetRequiredService<DatabaseHealthState>();

        Assert.IsFalse(health.IsHealthy, "Startup was healthy in a data directory nothing can be created in.");
        Assert.AreEqual(StartupDegradationReasons.DataDirectoryNotWritable, health.FailureReason);
    }

    /// <summary>
    /// #490, row 17: an index that disagrees with its table, which <c>quick_check</c> does not detect, degrades
    /// startup, and the check startup kept names the layer. Since #457 the operator's reason names it too.
    /// </summary>
    [TestMethod]
    public void Startup_IndexDisagreesWithItsTable_DegradesNamingTheLayer()
    {
        using WebApplicationFactory<Program> factory = FactoryWithAnIndexThatDisagreesWithItsTable();

        using HttpClient client = factory.CreateClient();
        DatabaseHealthState health = factory.Services.GetRequiredService<DatabaseHealthState>();

        Assert.IsFalse(health.IsHealthy, "Startup was healthy over an index that disagrees with its table.");
        Assert.AreEqual(DatabaseUnsoundGuidance.Reason(health.StartupCheck!.FirstFailure!, null), health.FailureReason);
        Assert.AreEqual(DatabaseCheckLayer.Indexes, health.StartupCheck?.FirstFailure?.Layer, "The startup check does not name the failing layer.");
    }

    /// <summary>
    /// #490, row 17, decision E: until #493 makes enforcement explicit, a dangling reference may be the
    /// application's own, so it does not degrade startup. The check startup kept still reports it, as a result
    /// rather than only a log line.
    /// </summary>
    [TestMethod]
    public async Task Startup_DanglingReference_StaysHealthyAndReportsTheLayer()
    {
        using WebApplicationFactory<Program> factory = FactoryWithADanglingReference();

        using HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync(HealthRoute, TestContext.CancellationToken);
        DatabaseHealthState health = factory.Services.GetRequiredService<DatabaseHealthState>();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, health.FailureReason);
        Assert.AreEqual(DatabaseCheckLayer.References, health.StartupCheck?.FirstFailure?.Layer, "The startup check does not report the dangling reference.");
    }

    private const string HealthRoute = "/api/v1/health";
    private const string VersionRoute = "/api/v1/version";
    private const string RandomQuoteRoute = "/api/v1/quotes/random";
    private const string AdminKey = "test-admin-key-305";

    /// <summary>
    /// A database a first run really created, then changed outside any migration by <paramref name="sabotage"/>.
    /// Foreign keys are off for the sabotage only: the first run wrote rows that reference the table being
    /// dropped, and the point is a missing object, not a refused drop.
    /// </summary>
    private WebApplicationFactory<Program> FactoryWithASchemaDifference(string sabotage, string? adminKey = null)
    {
        string dataDirectory = DataDirectoryWithAMigratedDatabase();

        using (IDbConnection connection =
            new SqliteConnectionFactory(Path.Combine(dataDirectory, DataPaths.DatabaseFile)).CreateConnection())
        {
            connection.Execute("PRAGMA foreign_keys = OFF;");
            connection.Execute(sabotage);
        }

        SqliteConnection.ClearAllPools();
        return adminKey is null ? FactoryFor(dataDirectory) : FactoryWithAdminKey(dataDirectory, adminKey);
    }

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

    /// <summary>A database a first run really created and migrated, its data directory returned for sabotage.</summary>
    private string DataDirectoryWithAMigratedDatabase()
    {
        string dataDirectory = NewDataDirectory();

        using (WebApplicationFactory<Program> firstRun = FactoryFor(dataDirectory))
        {
            using HttpClient warmUp = firstRun.CreateClient();
        }

        // Pooled connections hold the file open on Windows, which blocks the truncation below.
        SqliteConnection.ClearAllPools();
        return dataDirectory;
    }

    private WebApplicationFactory<Program> FactoryOverAnIntactDatabase() =>
        FactoryFor(DataDirectoryWithAMigratedDatabase());

    private WebApplicationFactory<Program> FactoryWithTruncatedDatabase()
    {
        string dataDirectory = DataDirectoryWithAMigratedDatabase();
        string database = Path.Combine(dataDirectory, DataPaths.DatabaseFile);

        // The sidecars go too: a surviving -wal beside a damaged main file gives SQLite two disagreeing
        // sources, and which it reports first would be timing rather than this test's subject.
        File.Delete(database + "-wal");
        File.Delete(database + "-shm");
        using (FileStream file = new(database, FileMode.Open, FileAccess.Write))
            file.SetLength(file.Length / 2);

        return FactoryFor(dataDirectory);
    }

    /// <summary>
    /// #490: a migrated database whose <c>-wal</c> holds data and whose <c>-shm</c> exists, in a data directory
    /// no file can be created in. Every file SQLite needs is already there and writable, so opening the
    /// database succeeds; only a probe of the directory shows it cannot be written. Denied per platform, as
    /// <see cref="FactoryWithKeysDirectoryNotWritable"/> does, and lifted in <see cref="RemoveTemporaryDirectories"/>.
    /// </summary>
    private WebApplicationFactory<Program> FactoryWithExistingSidecarsInAnUnwritableDataDirectory()
    {
        string dataDirectory = DataDirectoryWithAMigratedDatabase();
        string database = Path.Combine(dataDirectory, DataPaths.DatabaseFile);
        File.Delete(database + "-wal");
        File.Delete(database + "-shm");

        DocumentedFormatFixtures.AddWalHoldingData(database, "CREATE TABLE AddedInTheWal (Id INTEGER PRIMARY KEY);");
        File.WriteAllBytes(database + "-shm", []);

        if (OperatingSystem.IsWindows())
        {
            DirectoryInfo directory = new(dataDirectory);
            System.Security.AccessControl.DirectorySecurity security = directory.GetAccessControl();
            security.AddAccessRule(WriteDenial());
            directory.SetAccessControl(security);
        }
        else
        {
            File.SetUnixFileMode(dataDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }

        _deniedDirectories.Add(dataDirectory);
        Assert.IsFalse(DirectoryWriteProbe.CanCreateFileIn(dataDirectory), "Precondition: the data directory can still be written.");

        return FactoryFor(dataDirectory);
    }

    /// <summary>
    /// #490: a migrated database with an extra table whose index entry was rewritten in place, through the
    /// documented record format, so the index no longer agrees with its table. Extra objects leave the schema
    /// intact (#305), so this is the database's only fault.
    /// </summary>
    private WebApplicationFactory<Program> FactoryWithAnIndexThatDisagreesWithItsTable(params (string Key, string Value)[] settings)
    {
        string dataDirectory = DataDirectoryWithAMigratedDatabase();
        string database = Path.Combine(dataDirectory, DataPaths.DatabaseFile);
        File.Delete(database + "-wal");
        File.Delete(database + "-shm");

        DocumentedFormatFixtures.Execute(database,
            "CREATE TABLE CheckFixture (Id INTEGER PRIMARY KEY, Rank INTEGER NOT NULL);",
            "CREATE INDEX IX_CheckFixture_Rank ON CheckFixture (Rank);",
            "INSERT INTO CheckFixture (Id, Rank) VALUES (1, 10), (2, 20), (3, 30), (4, 40), (5, 50);");
        DocumentedFormatFixtures.RewriteLastIndexKey(database, "IX_CheckFixture_Rank", 100);

        return FactoryFor(dataDirectory, settings);
    }

    /// <summary>#490: a migrated database with an extra row that references one that does not exist.</summary>
    private WebApplicationFactory<Program> FactoryWithADanglingReference()
    {
        string dataDirectory = DataDirectoryWithAMigratedDatabase();
        string database = Path.Combine(dataDirectory, DataPaths.DatabaseFile);
        File.Delete(database + "-wal");
        File.Delete(database + "-shm");

        DocumentedFormatFixtures.Execute(database,
            "CREATE TABLE CheckParent (Id INTEGER PRIMARY KEY);",
            "CREATE TABLE CheckChild (Id INTEGER PRIMARY KEY, ParentId INTEGER NOT NULL REFERENCES CheckParent (Id));");
        DocumentedFormatFixtures.WriteWithoutForeignKeyEnforcement(database, "INSERT INTO CheckChild (Id, ParentId) VALUES (1, 999);");

        return FactoryFor(dataDirectory);
    }

    private WebApplicationFactory<Program> FactoryWithAMissingDomainTable()
    {
        string dataDirectory = DataDirectoryWithAMigratedDatabase();

        using (IDbConnection connection =
            new SqliteConnectionFactory(Path.Combine(dataDirectory, DataPaths.DatabaseFile)).CreateConnection())
        {
            connection.Execute("DROP TABLE Quotinator_Quote;");
        }

        SqliteConnection.ClearAllPools();
        return FactoryFor(dataDirectory);
    }

    /// <summary>
    /// A file where the data directory belongs, so <c>Directory.CreateDirectory</c> throws
    /// <c>IOException</c> before Kestrel binds, deterministically and identically on Windows and Linux.
    /// </summary>
    private WebApplicationFactory<Program> FactoryWithDataDirectoryPathOccupiedByAFile()
    {
        string dataDirectory = Path.Combine(NewDataDirectory(), "data");
        File.WriteAllText(dataDirectory, "not a directory");

        return FactoryFor(dataDirectory);
    }

    /// <summary>
    /// A file where the keys/ directory belongs. <c>Directory.CreateDirectory</c> then throws
    /// <c>IOException</c>, deterministically, and identically on Windows and Linux, before Kestrel binds.
    /// </summary>
    private WebApplicationFactory<Program> FactoryWithKeysDirectoryBlocked()
    {
        string dataDirectory = NewDataDirectory();
        File.WriteAllText(Path.Combine(dataDirectory, DataPaths.DataProtectionFolder), "not a directory");

        return FactoryFor(dataDirectory);
    }

    /// <summary>
    /// An empty <c>keys/</c> directory the test process cannot create files in. Denied per platform,
    /// since a read-only attribute does not stop file creation on Windows: an explicit deny ACE there,
    /// owner read and execute only elsewhere. The deny is lifted in <see cref="RemoveTemporaryDirectories"/>
    /// so the directory can be removed.
    /// </summary>
    private WebApplicationFactory<Program> FactoryWithKeysDirectoryNotWritable()
    {
        string dataDirectory = NewDataDirectory();
        string keys = Path.Combine(dataDirectory, DataPaths.DataProtectionFolder);
        Directory.CreateDirectory(keys);

        if (OperatingSystem.IsWindows())
        {
            DirectoryInfo directory = new(keys);
            System.Security.AccessControl.DirectorySecurity security = directory.GetAccessControl();
            security.AddAccessRule(WriteDenial());
            directory.SetAccessControl(security);
            _deniedDirectories.Add(keys);
        }
        else
        {
            File.SetUnixFileMode(keys, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            _deniedDirectories.Add(keys);
        }

        return FactoryFor(dataDirectory);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static System.Security.AccessControl.FileSystemAccessRule WriteDenial() =>
        new(System.Security.Principal.WindowsIdentity.GetCurrent().User!,
            System.Security.AccessControl.FileSystemRights.CreateFiles
            | System.Security.AccessControl.FileSystemRights.WriteData
            | System.Security.AccessControl.FileSystemRights.AppendData,
            System.Security.AccessControl.AccessControlType.Deny);

    private static void LiftWriteDenial(string keys)
    {
        if (OperatingSystem.IsWindows())
        {
            DirectoryInfo directory = new(keys);
            System.Security.AccessControl.DirectorySecurity security = directory.GetAccessControl();
            security.RemoveAccessRule(WriteDenial());
            directory.SetAccessControl(security);
        }
        else
        {
            File.SetUnixFileMode(keys, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private const string NotADatabase = "this file is not a SQLite database";

    private WebApplicationFactory<Program> FactoryWithCorruptDatabase(string? adminKey = null, bool blockTheSetAsideFolder = false)
    {
        string dataDirectory = NewDataDirectory();
        // Bytes that are not a SQLite file: the 16-byte header check fails and SQLite reports
        // SQLITE_NOTADB. Deliberately not a truncated real database: that reports SQLITE_CORRUPT from
        // a different code path, and producing one in-process would mean seeding a database first only
        // to chop it, which is slower and pins less. The container scenario covers truncation.
        File.WriteAllText(Path.Combine(dataDirectory, DataPaths.DatabaseFile), NotADatabase);

        // #482: a file where the reserved folder belongs, so creating it throws, deterministically and
        // identically on Windows and Linux, the technique FactoryWithKeysDirectoryBlocked uses.
        if (blockTheSetAsideFolder)
        {
            string backups = Path.Combine(dataDirectory, DataPaths.BackupsFolder);
            Directory.CreateDirectory(backups);
            File.WriteAllText(Path.Combine(backups, SetAsideFolderName), "not a directory");
        }

        return adminKey is null ? FactoryFor(dataDirectory) : FactoryWithAdminKey(dataDirectory, adminKey);
    }

    /// <summary>The reserved folder's name, written out: a contract with the user and #481, not a detail.</summary>
    private const string SetAsideFolderName = "unreadable";

    private static string DataDirectoryOf(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<IConfiguration>()["Quotinator:DataDir"]!;

    private static string[] SetAsideMainFiles(WebApplicationFactory<Program> factory)
    {
        string folder = Path.Combine(DataDirectoryOf(factory), DataPaths.BackupsFolder, SetAsideFolderName);
        return Directory.Exists(folder)
            ? [.. Directory.GetFiles(folder).Where(f => !f.EndsWith("-wal", StringComparison.Ordinal) && !f.EndsWith("-shm", StringComparison.Ordinal))]
            : [];
    }

    private Task<HttpResponseMessage> PostResetAsync(HttpClient client)
    {
        HttpRequestMessage reset = new(HttpMethod.Post, "/api/v1/admin/database/reset");
        reset.Headers.Add("X-Api-Key", AdminKey);
        return client.SendAsync(reset, TestContext.CancellationToken);
    }

    private static WebApplicationFactory<Program> FactoryWithAdminKey(string dataDirectory, string adminKey) =>
        new QuotinatorWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Quotinator:DataDir", dataDirectory);
            // In memory rather than UseSetting: a developer's appsettings.local.json is read after
            // UseSetting's value and would replace the key, so the reset would answer 401.
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["Quotinator:AdminApiKey"] = adminKey }));
        });

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

    private static WebApplicationFactory<Program> FactoryFor(string dataDirectory, params (string Key, string Value)[] settings) =>
        new QuotinatorWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Quotinator:DataDir", dataDirectory);
            foreach ((string key, string value) in settings)
                builder.UseSetting(key, value);
        });

    private string NewDataDirectory()
    {
        TempDirectory directory = new("quotinator_326_resilience_");
        _temporaryDirectories.Add(directory);
        return directory.Path;
    }

    public TestContext TestContext { get; set; }
}

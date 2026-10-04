using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quotinator.Data.Connections;
using Quotinator.Data.Enums;
using Quotinator.Data.Notifications;
using Quotinator.Data.Paths;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.Database;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// #348: the backup quota warning is raised and cleared by the same condition checks on every path that
/// can change the backups folder, and on <c>POST /api/v1/notifications/refresh</c>, which anyone may call.
/// <para>
/// Driven through the real host, so what is proven is that each path actually runs the checks, not only
/// that the check works (<c>BackupQuotaCheckTests</c> holds that). Each host has a data directory of its
/// own and a 1 GB ceiling, and the backups folder is filled with a sparse file, before or after startup
/// depending on which path the test is about. A test that needs the warning already open seeds it
/// directly, so a path that runs no check cannot satisfy its precondition.
/// </para>
/// </summary>
[TestClass]
public class BackupQuotaWarningTests
{
    private const string AdminKey = "backup-quota-warning-key";
    private const long OneGigabyte = 1_073_741_824L;

    public TestContext TestContext { get; set; } = null!;

    private TempDirectory _dataDir = null!;

    [TestInitialize]
    public void TestInitialize() => _dataDir = new TempDirectory("quotinator_backup_quota_warning_");

    [TestCleanup]
    public void TestCleanup() => _dataDir.Dispose();

    [TestMethod]
    public async Task Startup_InsideTheReserve_RaisesTheWarning()
    {
        FillTo(percentOfCeiling: 95);
        using WebApplicationFactory<Program> factory = FactoryFor();

        Assert.AreEqual(1, await OpenWarningsAsync(factory));
    }

    /// <summary>Raising the quota clears the warning at the next startup, with nothing deleted.</summary>
    [TestMethod]
    public async Task Startup_UnderARaisedQuota_ClearsTheWarning()
    {
        using (WebApplicationFactory<Program> first = FactoryFor(quotaPercent: 100))
            _ = first.Services;
        await SeedWarningIntoTheDatabaseFileAsync();

        using WebApplicationFactory<Program> second = FactoryFor(quotaPercent: 100);

        Assert.AreEqual(0, await OpenWarningsAsync(second));
    }

    /// <summary>
    /// Filled after startup, so only the Reset's own check can raise it. The Reset rebuilds every table, the
    /// notifications included, so a warning raised before it would be gone anyway.
    /// </summary>
    [TestMethod]
    public async Task Reset_InsideTheReserve_RaisesTheWarning()
    {
        using WebApplicationFactory<Program> factory = FactoryFor();
        _ = factory.Services;
        FillTo(percentOfCeiling: 95);

        HttpResponseMessage reset = await AdminClient(factory).PostAsync("/api/v1/admin/database/reset", content: null, TestContext.CancellationToken);

        Assert.AreEqual(1, await OpenWarningsAsync(factory), $"reset answered {(int)reset.StatusCode}");
    }

    [TestMethod]
    public async Task OnDemandBackup_InsideTheReserve_RaisesTheWarning()
    {
        using WebApplicationFactory<Program> factory = FactoryFor();
        _ = factory.Services;
        FillTo(percentOfCeiling: 95);

        HttpResponseMessage created = await AdminClient(factory).PostAsync("/api/v1/admin/backups/create", content: null, TestContext.CancellationToken);

        Assert.AreEqual(1, await OpenWarningsAsync(factory), $"create answered {(int)created.StatusCode}");
    }

    [TestMethod]
    public async Task Deletion_BringingTheFolderUnderTheQuota_ClearsTheWarning()
    {
        using WebApplicationFactory<Program> factory = FactoryFor();
        _ = factory.Services;
        FillTo(percentOfCeiling: 95);
        await SeedWarningAsync(factory);

        HttpResponseMessage deleted = await AdminClient(factory).DeleteAsync("/api/v1/admin/backups/filler.db", TestContext.CancellationToken);

        Assert.AreEqual(0, await OpenWarningsAsync(factory), $"delete answered {(int)deleted.StatusCode}");
    }

    /// <summary>Anyone may refresh, not only administrators (developer, 2026-09-26).</summary>
    [TestMethod]
    public async Task Refresh_AnswersWithoutAnAdminKey()
    {
        using WebApplicationFactory<Program> factory = FactoryFor();

        HttpResponseMessage response = await factory.CreateClient().PostAsync("/api/v1/notifications/refresh", content: null, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Backups removed by hand, outside the application: a refresh clears the warning without a restart.</summary>
    [TestMethod]
    public async Task Refresh_FolderBroughtUnderOutsideTheApplication_ClearsTheWarning()
    {
        using WebApplicationFactory<Program> factory = FactoryFor();
        _ = factory.Services;
        FillTo(percentOfCeiling: 95);
        await SeedWarningAsync(factory);
        File.Delete(FillerPath);

        await factory.CreateClient().PostAsync("/api/v1/notifications/refresh", content: null, TestContext.CancellationToken);

        Assert.AreEqual(0, await OpenWarningsAsync(factory));
    }

    [TestMethod]
    public async Task Refresh_FolderPushedAboveOutsideTheApplication_RaisesTheWarning()
    {
        using WebApplicationFactory<Program> factory = FactoryFor();
        _ = factory.Services;
        FillTo(percentOfCeiling: 95);

        await factory.CreateClient().PostAsync("/api/v1/notifications/refresh", content: null, TestContext.CancellationToken);

        Assert.AreEqual(1, await OpenWarningsAsync(factory));
    }

    [TestMethod]
    public async Task Refresh_ReportsWhatEachCheckDid()
    {
        using WebApplicationFactory<Program> factory = FactoryFor();
        _ = factory.Services;
        FillTo(percentOfCeiling: 95);

        HttpResponseMessage response = await factory.CreateClient().PostAsync("/api/v1/notifications/refresh", content: null, TestContext.CancellationToken);

        Assert.Contains("{\"kind\":\"backupquotareached\",\"outcome\":\"raised\"}", await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
    }

    /// <summary>A kind registered later is re-checked without the endpoint changing: proven with a test-only check.</summary>
    [TestMethod]
    public async Task Refresh_RunsEveryRegisteredCheck()
    {
        RecordingCheck recording = new();
        using WebApplicationFactory<Program> factory = FactoryFor().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<INotificationConditionCheck>(recording)));
        _ = factory.Services;
        recording.Ran = false;

        await factory.CreateClient().PostAsync("/api/v1/notifications/refresh", content: null, TestContext.CancellationToken);

        Assert.IsTrue(recording.Ran);
    }

    private string FillerPath => Path.Combine(_dataDir.Path, DataPaths.BackupsFolder, "filler.db");

    private void FillTo(int percentOfCeiling)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FillerPath)!);
        using FileStream filler = new(FillerPath, FileMode.Create, FileAccess.Write);
        filler.SetLength(OneGigabyte * percentOfCeiling / 100);
    }

    private WebApplicationFactory<Program> FactoryFor(int quotaPercent = 90) =>
        new QuotinatorWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Quotinator:DataDir", _dataDir.Path);
            builder.UseSetting("Quotinator:MaxBackupStorageGb", "1");
            builder.UseSetting("Quotinator:BackupQuotaPercent", quotaPercent.ToString(System.Globalization.CultureInfo.InvariantCulture));

            // The admin key is read from configuration when a request arrives, so it is supplied the way the
            // other endpoint tests do: UseSetting alone left every admin call answering 401.
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection([new KeyValuePair<string, string?>("Quotinator:AdminApiKey", AdminKey)]));
        });

    private static HttpClient AdminClient(WebApplicationFactory<Program> factory)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AdminKey);
        return client;
    }

    private static async Task<int> OpenWarningsAsync(WebApplicationFactory<Program> factory) =>
        (await factory.Services.GetRequiredService<INotificationReader>().GetActiveNotificationsAsync())
            .Count(n => n.MetadataKind?.Parsed == NotificationMetadataKind.BackupQuotaReached);

    private static async Task SeedWarningAsync(WebApplicationFactory<Program> factory) =>
        await SeedWarningAsync(factory.Services.GetRequiredService<INotificationReader>(), factory.Services.GetRequiredService<INotificationWriter>());

    private async Task SeedWarningIntoTheDatabaseFileAsync()
    {
        SqliteConnectionFactory connections = new(Path.Combine(_dataDir.Path, DataPaths.DatabaseFile));
        await SeedWarningAsync(Quotinator.Data.Testing.Database.TestNotificationReader.Create(connections), new NotificationWriter(connections));
    }

    private static async Task SeedWarningAsync(INotificationReader reader, INotificationWriter writer) =>
        await NotificationSeeding.SeedWhileUnresolvedAsync(
            reader, writer, NotificationType.Warning,
            new BackupQuotaReachedMetadataDto
            {
                UsedBytes = 1, QuotaBytes = 1, CeilingBytes = 1, ReleaseState = NotificationReleaseState.NotApplicable,
            },
            body: "seeded", appVersionId: null, dismissTrigger: NotificationDismissTrigger.BackupQuotaRestored);

    /// <summary>Records that it ran; changes nothing.</summary>
    private sealed class RecordingCheck : INotificationConditionCheck
    {
        /// <summary>Whether <see cref="CheckAsync"/> has been called since it was last reset.</summary>
        public bool Ran { get; set; }

        /// <inheritdoc/>
        public NotificationMetadataKind Kind => NotificationMetadataKind.BackupRefused;

        /// <inheritdoc/>
        public Task<NotificationConditionOutcome> CheckAsync()
        {
            Ran = true;
            return Task.FromResult(NotificationConditionOutcome.Unchanged);
        }
    }
}

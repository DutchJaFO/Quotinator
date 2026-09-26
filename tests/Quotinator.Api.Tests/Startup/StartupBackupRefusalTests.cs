using System.Data;
using System.Net;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Quotinator.Constants.Api;
using Quotinator.Data.Connections;
using Quotinator.Data.Enums;
using Quotinator.Data.Paths;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// #348: a startup whose pending migration cannot be backed up does not run it, and says so. The migration
/// is refused inside the initializer; these tests hold what the running application then reports, through
/// the real initializer and a real database, since a fake could only prove that <c>Program.cs</c> reads a
/// result the fake was told to return.
/// </summary>
[TestClass]
public class StartupBackupRefusalTests
{
    private const string HealthRoute = "/api/v1/health";

    private readonly List<string> _temporaryDirectories = [];
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    /// <summary>Factories first: a running host holds the database open, and the directory cannot go while it does.</summary>
    [TestCleanup]
    public void Cleanup()
    {
        foreach (WebApplicationFactory<Program> factory in _factories)
            factory.Dispose();

        foreach (string directory in _temporaryDirectories)
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { /* a still-open SQLite handle is not this test's concern */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>The schema is behind the build, so serving from it is not safe: the application degrades.</summary>
    [TestMethod]
    public async Task Startup_MigrationRefusedForBackup_ReportsUnhealthy()
    {
        HttpResponseMessage health = await HealthAfterARefusedMigrationAsync();

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, health.StatusCode);
    }

    [TestMethod]
    public async Task Startup_MigrationRefusedForBackup_ReasonNamesTheObstacle()
    {
        string body = await (await HealthAfterARefusedMigrationAsync()).Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.Contains(nameof(BackupOutcome.BudgetExceeded), body, StringComparison.Ordinal);
    }

    /// <summary>A full quota's remedy names the setting that raises it, so the operator is not left to guess.</summary>
    [TestMethod]
    public async Task Startup_MigrationRefusedForBackup_ReasonCarriesTheRemedies()
    {
        string body = await (await HealthAfterARefusedMigrationAsync()).Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.Contains("MaxBackupStorageGb", body, StringComparison.Ordinal);
    }

    /// <summary>The reason points at the Knowledgebase entry explaining every obstacle and how to resolve it.</summary>
    [TestMethod]
    public async Task Startup_MigrationRefusedForBackup_ReasonLinksTheKnowledgebaseEntry()
    {
        string body = await (await HealthAfterARefusedMigrationAsync()).Content.ReadAsStringAsync(TestContext.CancellationToken);

        Assert.Contains(KnowledgebaseLinks.NoBackupCouldBeTaken, body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A real database, migrated and loaded by a first start, then its newest recorded migration removed so
    /// the next start finds that migration pending. The second start has a zero backup quota, so no backup
    /// can be taken and the migration is refused before it would run again.
    /// </summary>
    private async Task<HttpResponseMessage> HealthAfterARefusedMigrationAsync()
    {
        string dataDirectory = NewDataDirectory();

        using (WebApplicationFactory<Program> firstRun = FactoryFor(dataDirectory, maxBackupStorageGb: 1))
        {
            using HttpClient warmUp = firstRun.CreateClient();
        }

        ForgetTheNewestMigration(dataDirectory);

        WebApplicationFactory<Program> factory = FactoryFor(dataDirectory, maxBackupStorageGb: 0);
        _factories.Add(factory);
        return await factory.CreateClient().GetAsync(HealthRoute, TestContext.CancellationToken);
    }

    private static void ForgetTheNewestMigration(string dataDirectory)
    {
        using IDbConnection connection =
            new SqliteConnectionFactory(Path.Combine(dataDirectory, DataPaths.DatabaseFile)).CreateConnection();

        connection.Execute(
            "DELETE FROM System_ConsumerSchemaVersion WHERE Version = (SELECT MAX(Version) FROM System_ConsumerSchemaVersion);");
    }

    private static WebApplicationFactory<Program> FactoryFor(string dataDirectory, int maxBackupStorageGb) =>
        new QuotinatorWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Quotinator:DataDir", dataDirectory);
            builder.UseSetting("Quotinator:MaxBackupStorageGb", maxBackupStorageGb.ToString(System.Globalization.CultureInfo.InvariantCulture));

            // Nothing here concerns downloading sources; left on, a slow upstream would hold startup.
            builder.UseSetting("Quotinator:AutoUpdateSources", "false");
        });

    private string NewDataDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "quotinator-348s-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _temporaryDirectories.Add(directory);
        return directory;
    }

    public TestContext TestContext { get; set; }
}

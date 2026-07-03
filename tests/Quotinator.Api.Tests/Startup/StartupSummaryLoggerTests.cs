using Microsoft.VisualStudio.TestTools.UnitTesting;
using Quotinator.Api.Startup;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Import;

namespace Quotinator.Api.Tests.Startup;

[TestClass]
public class StartupSummaryLoggerTests
{
    // -------------------------------------------------------------------------
    #region Helpers

    private static StartupSummaryLogger Build(
        CapturingLogger<StartupSummaryLogger> logger,
        string?  migrationApplied    = null,
        bool     adminKeyConfigured  = false,
        bool     sslEnabled          = false,
        bool     logRequests         = false,
        bool     isHa                = false)
    {
        var db      = new StubDbInitializer(migrationApplied);
        var version = new StubVersionService("1.2.3");
        return new StartupSummaryLogger(
            logger, db, version,
            dataDir:            "/data",
            dbPath:             "/data/quotinatordata.db",
            backupsDir:         "/data/backups",
            keysDir:            "/data/keys",
            logLevel:           "info",
            logRequests:        logRequests,
            sslEnabled:         sslEnabled,
            adminKeyConfigured: adminKeyConfigured,
            isHa:               isHa);
    }

    private sealed class StubVersionService(string version) : IVersionService
    {
        public string Version => version;
    }

    private sealed class StubDbInitializer(string? migrationApplied) : IDatabaseInitializer
    {
        public int    SchemaVersion    => 3;
        public int    DataSchemaVersion => 2;
        public int    QuoteCount       => 780;
        public int    SourceCount      => 3;
        public int    CharacterCount   => 42;
        public int    PeopleCount      => 12;
        public string? MigrationApplied => migrationApplied;
        public IReadOnlyList<SeedDuplicateRecord> LastSeedDuplicates => [];
        public Task InitialiseAsync()                    => Task.CompletedTask;
        public Task ReseedAsync()                        => Task.CompletedTask;
        public Task ResetAsync(bool preserveSchemaVersion = false) => Task.CompletedTask;
        public Task<SeedPreviewResult> PreviewSeedAsync() =>
            Task.FromResult(new SeedPreviewResult([], [], 0, 0));
    }

    private static string AllMessages(CapturingLogger<StartupSummaryLogger> logger)
        => string.Join("\n", logger.Messages);

    #endregion

    // -------------------------------------------------------------------------
    #region LogStarting — opening banner

    [TestMethod]
    public void LogStarting_LogsExactlyOneEntry()
    {
        var logger = new CapturingLogger<StartupSummaryLogger>();
        Build(logger).LogStarting();
        Assert.AreEqual(1, logger.Messages.Count);
    }

    [TestMethod]
    public void LogStarting_BannerContainsHashBorder()
    {
        var logger = new CapturingLogger<StartupSummaryLogger>();
        Build(logger).LogStarting();
        StringAssert.Contains(logger.Messages[0], "##############################");
    }

    [TestMethod]
    public void LogStarting_BannerContainsStartingText()
    {
        var logger = new CapturingLogger<StartupSummaryLogger>();
        Build(logger).LogStarting();
        StringAssert.Contains(logger.Messages[0], "Quotinator starting");
    }

    #endregion

    // -------------------------------------------------------------------------
    #region LogReady — listening lines before banner

    [TestMethod]
    public void LogReady_ListeningLinesLoggedBeforeBanner()
    {
        var logger = new CapturingLogger<StartupSummaryLogger>();
        Build(logger).LogReady(["http://0.0.0.0:8080"]);

        var listeningIdx = logger.Messages.FindIndex(m => m.Contains("listening on"));
        var bannerIdx    = logger.Messages.FindIndex(m => m.Contains("Quotinator ready"));
        Assert.IsTrue(listeningIdx >= 0,  "listening on line not found");
        Assert.IsTrue(bannerIdx    >= 0,  "ready banner not found");
        Assert.IsTrue(listeningIdx < bannerIdx, "listening line must come before the ready banner");
    }

    [TestMethod]
    public void LogReady_EmitsOneListeningLinePerAddress()
    {
        var logger = new CapturingLogger<StartupSummaryLogger>();
        Build(logger).LogReady(["http://0.0.0.0:8080", "https://0.0.0.0:8443"]);

        var listeningLines = logger.Messages.Where(m => m.Contains("listening on")).ToList();
        Assert.AreEqual(2, listeningLines.Count);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region LogReady — closing banner content

    [TestMethod]
    public void LogReady_BannerContainsHashBorder()
    {
        var logger = new CapturingLogger<StartupSummaryLogger>();
        Build(logger).LogReady(["http://0.0.0.0:8080"]);
        StringAssert.Contains(AllMessages(logger), "##############################");
    }

    [TestMethod]
    public void LogReady_BannerContainsReadyText()
    {
        var logger = new CapturingLogger<StartupSummaryLogger>();
        Build(logger).LogReady(["http://0.0.0.0:8080"]);
        StringAssert.Contains(AllMessages(logger), "Quotinator ready");
    }

    [TestMethod]
    public void LogReady_BannerContainsVersion()
    {
        var logger = new CapturingLogger<StartupSummaryLogger>();
        Build(logger).LogReady(["http://0.0.0.0:8080"]);
        StringAssert.Contains(AllMessages(logger), "1.2.3");
    }

    [TestMethod]
    public void LogReady_BannerContainsDbStats()
    {
        var logger = new CapturingLogger<StartupSummaryLogger>();
        Build(logger).LogReady(["http://0.0.0.0:8080"]);
        var all = AllMessages(logger);
        StringAssert.Contains(all, "schema v3");
        StringAssert.Contains(all, "780 quotes");
        StringAssert.Contains(all, "3 sources");
        StringAssert.Contains(all, "42 characters");
        StringAssert.Contains(all, "12 people");
    }

    [TestMethod]
    public void LogReady_BannerContainsMigrationLine_WhenMigrationApplied()
    {
        var logger = new CapturingLogger<StartupSummaryLogger>();
        Build(logger, migrationApplied: "v2 -> v3").LogReady(["http://0.0.0.0:8080"]);
        StringAssert.Contains(AllMessages(logger), "migration applied: v2 -> v3");
    }

    [TestMethod]
    public void LogReady_BannerOmitsMigrationLine_WhenNoMigration()
    {
        var logger = new CapturingLogger<StartupSummaryLogger>();
        Build(logger, migrationApplied: null).LogReady(["http://0.0.0.0:8080"]);
        Assert.IsFalse(AllMessages(logger).Contains("migration applied"),
            "migration line must not appear when no migration ran");
    }

    [TestMethod]
    public void LogReady_BannerContainsMcpNotImplemented()
    {
        var logger = new CapturingLogger<StartupSummaryLogger>();
        Build(logger).LogReady(["http://0.0.0.0:8080"]);
        StringAssert.Contains(AllMessages(logger), "MCP server:     not implemented");
    }

    [TestMethod]
    public void LogReady_BannerContainsLogLevel()
    {
        var logger = new CapturingLogger<StartupSummaryLogger>();
        Build(logger).LogReady(["http://0.0.0.0:8080"]);
        StringAssert.Contains(AllMessages(logger), "Log level:      info");
    }

    [TestMethod]
    public void LogReady_AdminKeySet_ShowsSet()
    {
        var logger = new CapturingLogger<StartupSummaryLogger>();
        Build(logger, adminKeyConfigured: true).LogReady(["http://0.0.0.0:8080"]);
        StringAssert.Contains(AllMessages(logger), "Admin API key:  set");
    }

    [TestMethod]
    public void LogReady_AdminKeyNotSet_ShowsNotSet()
    {
        var logger = new CapturingLogger<StartupSummaryLogger>();
        Build(logger, adminKeyConfigured: false).LogReady(["http://0.0.0.0:8080"]);
        StringAssert.Contains(AllMessages(logger), "Admin API key:  not set");
    }

    [TestMethod]
    public void LogReady_SslOn_ShowsOn()
    {
        var logger = new CapturingLogger<StartupSummaryLogger>();
        Build(logger, sslEnabled: true).LogReady(["http://0.0.0.0:8080"]);
        StringAssert.Contains(AllMessages(logger), "SSL:            on");
    }

    [TestMethod]
    public void LogReady_SslOff_ShowsOff()
    {
        var logger = new CapturingLogger<StartupSummaryLogger>();
        Build(logger, sslEnabled: false).LogReady(["http://0.0.0.0:8080"]);
        StringAssert.Contains(AllMessages(logger), "SSL:            off");
    }

    [TestMethod]
    public void LogReady_LogRequestsOn_ShowsOn()
    {
        var logger = new CapturingLogger<StartupSummaryLogger>();
        Build(logger, logRequests: true).LogReady(["http://0.0.0.0:8080"]);
        StringAssert.Contains(AllMessages(logger), "Log requests:   on");
    }

    [TestMethod]
    public void LogReady_BannerContainsRestApiUrl()
    {
        var logger = new CapturingLogger<StartupSummaryLogger>();
        Build(logger).LogReady(["http://0.0.0.0:8080"]);
        StringAssert.Contains(AllMessages(logger), "REST API:");
    }

    [TestMethod]
    public void LogReady_BannerContainsOpenApiUiUrl()
    {
        var logger = new CapturingLogger<StartupSummaryLogger>();
        Build(logger).LogReady(["http://0.0.0.0:8080"]);
        StringAssert.Contains(AllMessages(logger), "OpenAPI UI:");
    }

    [TestMethod]
    public void LogReady_BannerContainsOpenApiSpecUrl()
    {
        var logger = new CapturingLogger<StartupSummaryLogger>();
        Build(logger).LogReady(["http://0.0.0.0:8080"]);
        StringAssert.Contains(AllMessages(logger), "OpenAPI spec:");
    }

    #endregion

    // -------------------------------------------------------------------------
    #region ResolveUrls — HA ingress

    [TestMethod]
    public void ResolveUrls_HaMode_AllFieldsReturnHaMessage()
    {
        var (restApi, ui, spec) = StartupSummaryLogger.ResolveUrls(
            ["http://0.0.0.0:8080"], isHa: true, sslEnabled: false, localIp: "192.168.1.1");

        const string expected = "(HA ingress - URL determined at runtime)";
        Assert.AreEqual(expected, restApi);
        Assert.AreEqual(expected, ui);
        Assert.AreEqual(expected, spec);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region ResolveUrls — no addresses

    [TestMethod]
    public void ResolveUrls_NoAddresses_AllFieldsReturnNotAvailable()
    {
        var (restApi, ui, spec) = StartupSummaryLogger.ResolveUrls(
            [], isHa: false, sslEnabled: false, localIp: "192.168.1.1");

        const string expected = "(address not available)";
        Assert.AreEqual(expected, restApi);
        Assert.AreEqual(expected, ui);
        Assert.AreEqual(expected, spec);
    }

    [TestMethod]
    public void ResolveUrls_OnlyIngressPort8099_AllFieldsReturnNotAvailable()
    {
        var (restApi, ui, spec) = StartupSummaryLogger.ResolveUrls(
            ["http://0.0.0.0:8099"], isHa: false, sslEnabled: false, localIp: "192.168.1.1");

        const string expected = "(address not available)";
        Assert.AreEqual(expected, restApi);
        Assert.AreEqual(expected, ui);
        Assert.AreEqual(expected, spec);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region ResolveUrls — URL formatting

    [TestMethod]
    public void ResolveUrls_HttpWildcard_ReplacesWithLocalIp()
    {
        var (restApi, ui, spec) = StartupSummaryLogger.ResolveUrls(
            ["http://0.0.0.0:8080"], isHa: false, sslEnabled: false, localIp: "192.168.1.5");

        Assert.AreEqual("http://192.168.1.5:8080/api/v1/", restApi);
        Assert.AreEqual("http://192.168.1.5:8080/scalar/v1", ui);
        Assert.AreEqual("http://192.168.1.5:8080/openapi/v1.json", spec);
    }

    [TestMethod]
    public void ResolveUrls_IPv6Wildcard_ReplacesWithLocalIp()
    {
        var (restApi, ui, spec) = StartupSummaryLogger.ResolveUrls(
            ["http://[::]:8080"], isHa: false, sslEnabled: false, localIp: "192.168.1.5");

        Assert.AreEqual("http://192.168.1.5:8080/api/v1/", restApi);
        Assert.AreEqual("http://192.168.1.5:8080/scalar/v1", ui);
        Assert.AreEqual("http://192.168.1.5:8080/openapi/v1.json", spec);
    }

    [TestMethod]
    public void ResolveUrls_SslEnabled_UsesHttpsScheme()
    {
        var (restApi, ui, spec) = StartupSummaryLogger.ResolveUrls(
            ["http://0.0.0.0:8080"], isHa: false, sslEnabled: true, localIp: "192.168.1.5");

        Assert.AreEqual("https://192.168.1.5:8080/api/v1/", restApi);
        Assert.AreEqual("https://192.168.1.5:8080/scalar/v1", ui);
        Assert.AreEqual("https://192.168.1.5:8080/openapi/v1.json", spec);
    }

    [TestMethod]
    public void ResolveUrls_MultipleAddresses_UsesPrimarySkippingIngressPort()
    {
        var (restApi, _, _) = StartupSummaryLogger.ResolveUrls(
            ["http://0.0.0.0:8099", "http://0.0.0.0:8080"],
            isHa: false, sslEnabled: false, localIp: "192.168.1.5");

        // 8080 is the primary; 8099 is the HA ingress port and must be skipped
        Assert.AreEqual("http://192.168.1.5:8080/api/v1/", restApi);
    }

    [TestMethod]
    public void ResolveUrls_LocalhostAddress_PassedThrough()
    {
        var (restApi, _, _) = StartupSummaryLogger.ResolveUrls(
            ["http://localhost:5000"], isHa: false, sslEnabled: false, localIp: "192.168.1.5");

        // Non-wildcard address is not replaced — it passes through as-is
        Assert.AreEqual("http://localhost:5000/api/v1/", restApi);
    }

    #endregion
}

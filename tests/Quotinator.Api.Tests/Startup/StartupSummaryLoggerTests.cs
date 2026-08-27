using Quotinator.Data.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
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

    /// <summary>
    /// Builds the logger against a real Serilog pipeline via <see cref="CaptureSink"/> — a plain MEL
    /// test double's formatter callback does not apply Serilog's default string-quoting behaviour, so
    /// it cannot catch a missing <c>{:l}</c> literal specifier (#244 found this live: the banner's
    /// `$"""..."""` → message-template conversion initially omitted `:l` on every string placeholder,
    /// and every existing `Contains(...)` assertion here still passed since a quoted value still
    /// contains its own unquoted substring).
    /// </summary>
    private static (StartupSummaryLogger Logger, CaptureSink Sink) Build(
        string?  migrationApplied    = null,
        bool     adminKeyConfigured  = false,
        bool     sslEnabled          = false,
        bool     logRequests         = false,
        bool     isHa                = false)
    {
        CaptureSink sink    = new CaptureSink();
        Serilog.Core.Logger serilog = new LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Information)
            .WriteTo.Sink(sink)
            .CreateLogger();
        ILogger<StartupSummaryLogger> logger = new SerilogLoggerFactory(serilog)
            .CreateLogger<StartupSummaryLogger>();

        StubDbInitializer db      = new StubDbInitializer(migrationApplied);
        StubVersionService version = new StubVersionService("1.2.3");
        StartupSummaryLogger startupLogger = new StartupSummaryLogger(
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
        return (startupLogger, sink);
    }

    private sealed class StubVersionService(string version) : IVersionService
    {
        public string Version => version;
        public string Application => "Quotinator.Api";
    }

    private sealed class StubDbInitializer(string? migrationApplied) : IDatabaseInitializer
    {
        public int    SchemaVersion    => 3;
        public int    DataSchemaVersion => 2;
        public int    QuoteCount       => 780;
        public int    SourceCount      => 3;
        public int    CharacterCount   => 42;
        public int    PeopleCount      => 12;
        public int    SeriesCount      => 0;
        public int    UniverseCount    => 0;
        public int    StageDirectionCount => 0;
        public int    SoundCueCount    => 0;
        public int    ConversationCount => 0;
        public string? MigrationApplied => migrationApplied;
        public bool SchemaVersionOvershootDetected => false;
        public IReadOnlyList<FileImportReport> LastSeedReport => [];
        public Task<DatabaseOperationResult> InitialiseAsync() => Task.FromResult(DatabaseOperationResult.Success());

        public BackupOutcome CheckBackupReadiness(bool allowReserve = false) => BackupOutcome.Succeeded;
        public Task ReseedAsync(bool forceSourceRefresh = false) => Task.CompletedTask;
        public Task<DatabaseOperationResult> ResetAsync(bool preserveSchemaVersion = false, bool forceSourceRefresh = false, bool allowNoBackup = false) => Task.FromResult(DatabaseOperationResult.Success());
        public Task<SeedPreviewResult> PreviewSeedAsync() =>
            Task.FromResult(new SeedPreviewResult([], []));
        public Task<SourceCacheResolution> RefreshSourcesAsync(bool force = false) =>
            Task.FromResult(new SourceCacheResolution([], []));
    }

    private static string AllMessages(CaptureSink sink)
        => string.Join("\n", sink.Lines);

    #endregion

    // -------------------------------------------------------------------------
    #region LogStarting — opening banner

    [TestMethod]
    public void LogStarting_LogsExactlyOneEntry()
    {
        (StartupSummaryLogger logger, CaptureSink sink) = Build();
        logger.LogStarting();
        Assert.HasCount(1, sink.Lines);
    }

    [TestMethod]
    public void LogStarting_BannerContainsHashBorder()
    {
        (StartupSummaryLogger logger, CaptureSink sink) = Build();
        logger.LogStarting();
        Assert.Contains("##############################", sink.Lines[0]);
    }

    [TestMethod]
    public void LogStarting_BannerContainsStartingText()
    {
        (StartupSummaryLogger logger, CaptureSink sink) = Build();
        logger.LogStarting();
        Assert.Contains("Quotinator starting", sink.Lines[0]);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region LogReady — listening lines before banner

    [TestMethod]
    public void LogReady_ListeningLinesLoggedBeforeBanner()
    {
        (StartupSummaryLogger logger, CaptureSink sink) = Build();
        logger.LogReady(["http://0.0.0.0:8080"]);

        int listeningIdx = sink.Lines.ToList().FindIndex(m => m.Contains("listening on"));
        int bannerIdx    = sink.Lines.ToList().FindIndex(m => m.Contains("Quotinator ready"));
        Assert.IsGreaterThanOrEqualTo(0, listeningIdx,  "listening on line not found");
        Assert.IsGreaterThanOrEqualTo(0, bannerIdx,  "ready banner not found");
        Assert.IsLessThan(bannerIdx, listeningIdx, "listening line must come before the ready banner");
    }

    [TestMethod]
    public void LogReady_EmitsOneListeningLinePerAddress()
    {
        (StartupSummaryLogger logger, CaptureSink sink) = Build();
        logger.LogReady(["http://0.0.0.0:8080", "https://0.0.0.0:8443"]);

        List<string> listeningLines = [.. sink.Lines.Where(m => m.Contains("listening on"))];
        Assert.HasCount(2, listeningLines);
    }

    /// <summary>#244: the listening-address line is a string property — must carry the `{:l}`
    /// literal specifier, or Serilog wraps the address in quotes.</summary>
    [TestMethod]
    public void LogReady_ListeningLine_AddressNotQuoted()
    {
        (StartupSummaryLogger logger, CaptureSink sink) = Build();
        logger.LogReady(["http://0.0.0.0:8080"]);

        string listeningLine = sink.Lines.Single(m => m.Contains("listening on"));
        Assert.DoesNotContain("\"http://0.0.0.0:8080\"", listeningLine);
        Assert.Contains("listening on http://0.0.0.0:8080", listeningLine);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region LogReady — closing banner content

    [TestMethod]
    public void LogReady_BannerContainsHashBorder()
    {
        (StartupSummaryLogger logger, CaptureSink sink) = Build();
        logger.LogReady(["http://0.0.0.0:8080"]);
        Assert.Contains("##############################", AllMessages(sink));
    }

    [TestMethod]
    public void LogReady_BannerContainsReadyText()
    {
        (StartupSummaryLogger logger, CaptureSink sink) = Build();
        logger.LogReady(["http://0.0.0.0:8080"]);
        Assert.Contains("Quotinator ready", AllMessages(sink));
    }

    [TestMethod]
    public void LogReady_BannerContainsVersion()
    {
        (StartupSummaryLogger logger, CaptureSink sink) = Build();
        logger.LogReady(["http://0.0.0.0:8080"]);
        Assert.Contains("Version:        1.2.3", AllMessages(sink));
    }

    [TestMethod]
    public void LogReady_BannerContainsDbStats()
    {
        (StartupSummaryLogger logger, CaptureSink sink) = Build();
        logger.LogReady(["http://0.0.0.0:8080"]);
        string all = AllMessages(sink);
        Assert.Contains("schema v3", all);
        Assert.Contains("Statistics:", all);
        Assert.Contains("780 quotes", all);
        Assert.Contains("3 sources", all);
        Assert.Contains("42 characters", all);
        Assert.Contains("12 people", all);
    }

    /// <summary>#221: the five entity-type counts added alongside quotes/sources/characters/people
    /// each get their own line under the "Statistics:" section, not crammed onto the schema line —
    /// found live via T1 that a single-line format doesn't scale as more entity types are added.</summary>
    [TestMethod]
    public void LogReady_BannerContainsNewEntityTypeStats_OnePerLine()
    {
        (StartupSummaryLogger logger, CaptureSink sink) = Build();
        logger.LogReady(["http://0.0.0.0:8080"]);
        string all = AllMessages(sink);
        Assert.Contains("0 series", all);
        Assert.Contains("0 universes", all);
        Assert.Contains("0 stage directions", all);
        Assert.Contains("0 sound cues", all);
        Assert.Contains("0 conversations", all);
    }

    [TestMethod]
    public void LogReady_BannerContainsMigrationLine_WhenMigrationApplied()
    {
        (StartupSummaryLogger logger, CaptureSink sink) = Build(migrationApplied: "v2 -> v3");
        logger.LogReady(["http://0.0.0.0:8080"]);
        Assert.Contains("migration applied: v2 -> v3", AllMessages(sink));
    }

    [TestMethod]
    public void LogReady_BannerOmitsMigrationLine_WhenNoMigration()
    {
        (StartupSummaryLogger logger, CaptureSink sink) = Build(migrationApplied: null);
        logger.LogReady(["http://0.0.0.0:8080"]);
        Assert.DoesNotContain("migration applied", AllMessages(sink),
            "migration line must not appear when no migration ran");
    }

    /// <summary>#244: found live via T2 — the empty-string `MigLine` value rendered as a literal `""`
    /// pair when the `{MigLine}` placeholder was missing its `:l` specifier (Serilog quotes an empty
    /// string the same as any other string). The schema line must end cleanly with no stray quotes.</summary>
    [TestMethod]
    public void LogReady_SchemaLine_NoStrayQuotesWhenNoMigration()
    {
        (StartupSummaryLogger logger, CaptureSink sink) = Build(migrationApplied: null);
        logger.LogReady(["http://0.0.0.0:8080"]);
        string schemaLine = AllMessages(sink).Split('\n').Single(l => l.Contains("schema v3"));
        Assert.DoesNotContain("\"", schemaLine);
    }

    [TestMethod]
    public void LogReady_BannerContainsMcpNotImplemented()
    {
        (StartupSummaryLogger logger, CaptureSink sink) = Build();
        logger.LogReady(["http://0.0.0.0:8080"]);
        Assert.Contains("MCP server:     not implemented", AllMessages(sink));
    }

    [TestMethod]
    public void LogReady_BannerContainsLogLevel()
    {
        (StartupSummaryLogger logger, CaptureSink sink) = Build();
        logger.LogReady(["http://0.0.0.0:8080"]);
        Assert.Contains("Log level:      info", AllMessages(sink));
    }

    [TestMethod]
    public void LogReady_AdminKeySet_ShowsSet()
    {
        (StartupSummaryLogger logger, CaptureSink sink) = Build(adminKeyConfigured: true);
        logger.LogReady(["http://0.0.0.0:8080"]);
        Assert.Contains("Admin API key:  set", AllMessages(sink));
    }

    [TestMethod]
    public void LogReady_AdminKeyNotSet_ShowsNotSet()
    {
        (StartupSummaryLogger logger, CaptureSink sink) = Build(adminKeyConfigured: false);
        logger.LogReady(["http://0.0.0.0:8080"]);
        Assert.Contains("Admin API key:  not set", AllMessages(sink));
    }

    [TestMethod]
    public void LogReady_SslOn_ShowsOn()
    {
        (StartupSummaryLogger logger, CaptureSink sink) = Build(sslEnabled: true);
        logger.LogReady(["http://0.0.0.0:8080"]);
        Assert.Contains("SSL:            on", AllMessages(sink));
    }

    [TestMethod]
    public void LogReady_SslOff_ShowsOff()
    {
        (StartupSummaryLogger logger, CaptureSink sink) = Build(sslEnabled: false);
        logger.LogReady(["http://0.0.0.0:8080"]);
        Assert.Contains("SSL:            off", AllMessages(sink));
    }

    [TestMethod]
    public void LogReady_LogRequestsOn_ShowsOn()
    {
        (StartupSummaryLogger logger, CaptureSink sink) = Build(logRequests: true);
        logger.LogReady(["http://0.0.0.0:8080"]);
        Assert.Contains("Log requests:   on", AllMessages(sink));
    }

    [TestMethod]
    public void LogReady_BannerContainsRestApiUrl()
    {
        (StartupSummaryLogger logger, CaptureSink sink) = Build();
        logger.LogReady(["http://0.0.0.0:8080"]);
        Assert.Contains("REST API:", AllMessages(sink));
    }

    [TestMethod]
    public void LogReady_BannerContainsOpenApiUiUrl()
    {
        (StartupSummaryLogger logger, CaptureSink sink) = Build();
        logger.LogReady(["http://0.0.0.0:8080"]);
        Assert.Contains("OpenAPI UI:", AllMessages(sink));
    }

    [TestMethod]
    public void LogReady_BannerContainsOpenApiSpecUrl()
    {
        (StartupSummaryLogger logger, CaptureSink sink) = Build();
        logger.LogReady(["http://0.0.0.0:8080"]);
        Assert.Contains("OpenAPI spec:", AllMessages(sink));
    }

    /// <summary>#244: every string-valued field in the closing banner must render unquoted — the
    /// `:l` literal specifier on every string placeholder, proven against Serilog's real rendering
    /// rather than a MEL test double that can't reproduce the quoting behaviour at all.</summary>
    [TestMethod]
    public void LogReady_BannerFields_NoneAreQuoted()
    {
        (StartupSummaryLogger logger, CaptureSink sink) = Build(migrationApplied: "v2 -> v3", adminKeyConfigured: true, sslEnabled: true, logRequests: true);
        logger.LogReady(["http://0.0.0.0:8080"]);
        string all = AllMessages(sink);

        Assert.DoesNotContain("\"1.2.3\"", all);
        Assert.DoesNotContain("\"/data\"", all);
        Assert.DoesNotContain("\"/data/quotinatordata.db\"", all);
        Assert.DoesNotContain("\"/data/backups\"", all);
        Assert.DoesNotContain("\"/data/keys\"", all);
        Assert.DoesNotContain("\"info\"", all);
        Assert.DoesNotContain("\"on\"", all);
        Assert.DoesNotContain("\"set\"", all);
        Assert.DoesNotContain("v2 -> v3\"", all);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region ResolveUrls — HA ingress

    [TestMethod]
    public void ResolveUrls_HaMode_AllFieldsReturnHaMessage()
    {
        (string restApi, string ui, string spec) = StartupSummaryLogger.ResolveUrls(
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
        (string restApi, string ui, string spec) = StartupSummaryLogger.ResolveUrls(
            [], isHa: false, sslEnabled: false, localIp: "192.168.1.1");

        const string expected = "(address not available)";
        Assert.AreEqual(expected, restApi);
        Assert.AreEqual(expected, ui);
        Assert.AreEqual(expected, spec);
    }

    [TestMethod]
    public void ResolveUrls_OnlyIngressPort8099_AllFieldsReturnNotAvailable()
    {
        (string restApi, string ui, string spec) = StartupSummaryLogger.ResolveUrls(
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
        (string restApi, string ui, string spec) = StartupSummaryLogger.ResolveUrls(
            ["http://0.0.0.0:8080"], isHa: false, sslEnabled: false, localIp: "192.168.1.5");

        Assert.AreEqual("http://192.168.1.5:8080/api/v1/", restApi);
        Assert.AreEqual("http://192.168.1.5:8080/scalar/v1", ui);
        Assert.AreEqual("http://192.168.1.5:8080/openapi/v1.json", spec);
    }

    [TestMethod]
    public void ResolveUrls_IPv6Wildcard_ReplacesWithLocalIp()
    {
        (string restApi, string ui, string spec) = StartupSummaryLogger.ResolveUrls(
            ["http://[::]:8080"], isHa: false, sslEnabled: false, localIp: "192.168.1.5");

        Assert.AreEqual("http://192.168.1.5:8080/api/v1/", restApi);
        Assert.AreEqual("http://192.168.1.5:8080/scalar/v1", ui);
        Assert.AreEqual("http://192.168.1.5:8080/openapi/v1.json", spec);
    }

    [TestMethod]
    public void ResolveUrls_SslEnabled_UsesHttpsScheme()
    {
        (string restApi, string ui, string spec) = StartupSummaryLogger.ResolveUrls(
            ["http://0.0.0.0:8080"], isHa: false, sslEnabled: true, localIp: "192.168.1.5");

        Assert.AreEqual("https://192.168.1.5:8080/api/v1/", restApi);
        Assert.AreEqual("https://192.168.1.5:8080/scalar/v1", ui);
        Assert.AreEqual("https://192.168.1.5:8080/openapi/v1.json", spec);
    }

    [TestMethod]
    public void ResolveUrls_MultipleAddresses_UsesPrimarySkippingIngressPort()
    {
        (string restApi, _, _) = StartupSummaryLogger.ResolveUrls(
            ["http://0.0.0.0:8099", "http://0.0.0.0:8080"],
            isHa: false, sslEnabled: false, localIp: "192.168.1.5");

        // 8080 is the primary; 8099 is the HA ingress port and must be skipped
        Assert.AreEqual("http://192.168.1.5:8080/api/v1/", restApi);
    }

    [TestMethod]
    public void ResolveUrls_LocalhostAddress_PassedThrough()
    {
        (string restApi, _, _) = StartupSummaryLogger.ResolveUrls(
            ["http://localhost:5000"], isHa: false, sslEnabled: false, localIp: "192.168.1.5");

        // Non-wildcard address is not replaced — it passes through as-is
        Assert.AreEqual("http://localhost:5000/api/v1/", restApi);
    }

    #endregion
}

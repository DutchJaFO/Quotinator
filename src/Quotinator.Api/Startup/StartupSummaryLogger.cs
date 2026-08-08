using Quotinator.Core.Services;
using Quotinator.Data.Database;

namespace Quotinator.Api.Startup;

/// <summary>Logs the opening and closing startup banners.</summary>
/// <remarks>Initialises the summary logger with the config values captured at startup.</remarks>
/// <param name="logger">Logger the opening/closing banners and listen-address lines are written to.</param>
/// <param name="db">Database initialiser consulted for schema version, migration, and row-count statistics in the closing banner.</param>
/// <param name="version">Service reporting the running application version.</param>
/// <param name="dataDir">Data directory path, shown in the closing banner.</param>
/// <param name="dbPath">Database file path, shown in the closing banner.</param>
/// <param name="backupsDir">Backups directory path, shown in the closing banner.</param>
/// <param name="keysDir">DataProtection keys directory path, shown in the closing banner.</param>
/// <param name="logLevel">Configured log level, shown in the closing banner.</param>
/// <param name="logRequests">Whether request logging is enabled, shown in the closing banner.</param>
/// <param name="sslEnabled">Whether Kestrel HTTPS is enabled, shown in the closing banner and used to resolve the displayed URLs' scheme.</param>
/// <param name="adminKeyConfigured">Whether an admin API key is configured, shown in the closing banner.</param>
/// <param name="isHa">Whether the app is running as a Home Assistant add-on, used to resolve the displayed URLs.</param>
internal sealed class StartupSummaryLogger(
    ILogger<StartupSummaryLogger> logger,
    IDatabaseInitializer db,
    IVersionService version,
    string dataDir,
    string dbPath,
    string backupsDir,
    string keysDir,
    string logLevel,
    bool logRequests,
    bool sslEnabled,
    bool adminKeyConfigured,
    bool isHa)
{
    private readonly ILogger<StartupSummaryLogger> _logger = logger;
    private readonly IDatabaseInitializer _db = db;
    private readonly IVersionService _version = version;
    private readonly string _dataDir = dataDir;
    private readonly string _dbPath = dbPath;
    private readonly string _backupsDir = backupsDir;
    private readonly string _keysDir = keysDir;
    private readonly string _logLevel = logLevel;
    private readonly bool _logRequests = logRequests;
    private readonly bool _sslEnabled = sslEnabled;
    private readonly bool _adminKeyConfigured = adminKeyConfigured;
    private readonly bool _isHa = isHa;

    /// <summary>Logs the opening banner as a single entry before database initialisation.</summary>
    public void LogStarting() =>
        _logger.LogInformation(
            """

            ##############################
            #    Quotinator starting     #
            ##############################
            """);

    /// <summary>
    /// Logs <c>[Server] listening on</c> for each bound address, then logs the full
    /// closing banner as a single entry.
    /// </summary>
    public void LogReady(IReadOnlyCollection<string> boundAddresses)
    {
        var (restApi, openApiUi, openApiSpec) =
            ResolveUrls(boundAddresses, _isHa, _sslEnabled, GetLocalIp());

        foreach (var addr in boundAddresses)
            _logger.LogInformation("[Server] listening on {Address}", addr);

        var migLine = _db.MigrationApplied is { } mig
            ? $"\n                migration applied: {mig}"
            : string.Empty;

        _logger.LogInformation(
            """

            ##############################
            #     Quotinator ready       #
            ##############################
            Version:        {Version}
            Data:           {DataDir}
            Database:       {DbPath}
                            schema v{SchemaVersion} (data v{DataSchemaVersion}){MigLine}
            Statistics:
                            {QuoteCount} quotes
                            {SourceCount} sources
                            {CharacterCount} characters
                            {PeopleCount} people
                            {SeriesCount} series
                            {UniverseCount} universes
                            {StageDirectionCount} stage directions
                            {SoundCueCount} sound cues
                            {ConversationCount} conversations
            Backups:        {BackupsDir}
            DataProtection: {KeysDir}
            ------------------------------
            Log level:      {ConfiguredLogLevel}
            Log requests:   {LogRequests}
            SSL:            {Ssl}
            Admin API key:  {AdminApiKey}
            ------------------------------
            REST API:       {RestApi}
            OpenAPI UI:     {OpenApiUi}
            OpenAPI spec:   {OpenApiSpec}
            MCP server:     not implemented
            ##############################
            """,
            _version.Version, _dataDir, _dbPath, _db.SchemaVersion, _db.DataSchemaVersion, migLine,
            _db.QuoteCount, _db.SourceCount, _db.CharacterCount, _db.PeopleCount, _db.SeriesCount,
            _db.UniverseCount, _db.StageDirectionCount, _db.SoundCueCount, _db.ConversationCount,
            _backupsDir, _keysDir, _logLevel, _logRequests ? "on" : "off", _sslEnabled ? "on" : "off",
            _adminKeyConfigured ? "set" : "not set", restApi, openApiUi, openApiSpec);
    }

    /// <summary>
    /// Resolves display URLs from the bound Kestrel addresses. Internal for unit testing —
    /// accepts <paramref name="localIp"/> as a parameter so tests can pass a known value
    /// without performing network I/O.
    /// </summary>
    internal static (string RestApi, string OpenApiUi, string OpenApiSpec) ResolveUrls(
        IReadOnlyCollection<string> addresses, bool isHa, bool sslEnabled, string localIp)
    {
        const string ha = "(HA ingress - URL determined at runtime)";
        if (isHa) return (ha, ha, ha);

        var primary = addresses.FirstOrDefault(a => !a.Contains(":8099"));
        if (primary is null)
        {
            const string na = "(address not available)";
            return (na, na, na);
        }

        var scheme  = sslEnabled ? "https" : "http";
        var baseUrl = primary
            .Replace("http://0.0.0.0",  $"{scheme}://{localIp}")
            .Replace("http://[::]",     $"{scheme}://{localIp}")
            .Replace("https://0.0.0.0", $"{scheme}://{localIp}")
            .Replace("https://[::]",    $"{scheme}://{localIp}")
            .TrimEnd('/');

        return ($"{baseUrl}/api/v1/", $"{baseUrl}/scalar/v1", $"{baseUrl}/openapi/v1.json");
    }

    private static string GetLocalIp()
    {
        try
        {
            using var socket = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            return ((System.Net.IPEndPoint)socket.LocalEndPoint!).Address.ToString();
        }
        catch { return "localhost"; }
    }
}

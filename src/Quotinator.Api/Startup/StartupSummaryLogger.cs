using Quotinator.Core.Data;
using Quotinator.Core.Services;

namespace Quotinator.Api.Startup;

/// <summary>Logs the opening and closing startup banners.</summary>
internal sealed class StartupSummaryLogger
{
    private readonly ILogger<StartupSummaryLogger> _logger;
    private readonly IDatabaseInitializer          _db;
    private readonly IVersionService               _version;
    private readonly string _dataDir;
    private readonly string _dbPath;
    private readonly string _backupsDir;
    private readonly string _keysDir;
    private readonly string _logLevel;
    private readonly bool   _logRequests;
    private readonly bool   _sslEnabled;
    private readonly bool   _adminKeyConfigured;
    private readonly bool   _isHa;

    /// <summary>Initialises the summary logger with the config values captured at startup.</summary>
    public StartupSummaryLogger(
        ILogger<StartupSummaryLogger> logger,
        IDatabaseInitializer          db,
        IVersionService               version,
        string dataDir,
        string dbPath,
        string backupsDir,
        string keysDir,
        string logLevel,
        bool   logRequests,
        bool   sslEnabled,
        bool   adminKeyConfigured,
        bool   isHa)
    {
        _logger             = logger;
        _db                 = db;
        _version            = version;
        _dataDir            = dataDir;
        _dbPath             = dbPath;
        _backupsDir         = backupsDir;
        _keysDir            = keysDir;
        _logLevel           = logLevel;
        _logRequests        = logRequests;
        _sslEnabled         = sslEnabled;
        _adminKeyConfigured = adminKeyConfigured;
        _isHa               = isHa;
    }

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
            $"""

            ##############################
            #     Quotinator ready       #
            ##############################
            Version:        {_version.Version}
            Data:           {_dataDir}
            Database:       {_dbPath}
                            schema v{_db.SchemaVersion} - {_db.QuoteCount} quotes  {_db.SourceCount} sources  {_db.CharacterCount} characters  {_db.PeopleCount} people{migLine}
            Backups:        {_backupsDir}
            DataProtection: {_keysDir}
            ------------------------------
            Log level:      {_logLevel}
            Log requests:   {(_logRequests ? "on" : "off")}
            SSL:            {(_sslEnabled ? "on" : "off")}
            Admin API key:  {(_adminKeyConfigured ? "set" : "not set")}
            ------------------------------
            REST API:       {restApi}
            OpenAPI UI:     {openApiUi}
            OpenAPI spec:   {openApiSpec}
            MCP server:     not implemented
            ##############################
            """);
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

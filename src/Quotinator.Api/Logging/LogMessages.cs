namespace Quotinator.Api.Logging;

/// <summary>
/// Logging message templates specific to Quotinator.Api. Shapes that recur identically across
/// projects (a subsystem tag plus a page/pageSize pair, or plus a bare id) live in
/// <see cref="Quotinator.Logging.LogMessages"/> instead — see docs/logging.md's
/// "Logging call-site pattern" section for the decision procedure.
/// </summary>
internal static partial class LogMessages
{
    /// <summary>Logs an id+lang query entry, shared by every quote/conversation GetById-style endpoint.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "{Tag:l} id={Id} lang={Lang}")]
    public static partial void LogIdWithLang(this ILogger logger, string tag, string id, string? lang);

    /// <summary>Logs entry to the random-quote endpoint.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Api - Random] n={N} type={Type} genre={Genre} lang={Lang}")]
    public static partial void LogRandomQuoteQuery(this ILogger logger, string? n, string[]? type, string[]? genre, string? lang);

    /// <summary>Logs entry to the quote search endpoint.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Api - Search] q={Q} field={Field} limit={Limit} type={Type} lang={Lang}")]
    public static partial void LogSearchQuery(this ILogger logger, string? q, string? field, string? limit, string[]? type, string? lang);

    /// <summary>Logs entry to the quote GetAll endpoint (its own shape — page/pageSize plus type/lang — isn't shared with the generic masterdata GetAll shape).</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Api - GetAll] page={Page} pageSize={PageSize} type={Type} lang={Lang}")]
    public static partial void LogGetAllQuotesQuery(this ILogger logger, string? page, string? pageSize, string[]? type, string? lang);

    /// <summary>Logs entry to the import endpoint before a file is validated.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Api - Import] preview={Preview} file={File}")]
    public static partial void LogImportPreviewRequest(this ILogger logger, bool preview, string? file);

    /// <summary>Logs applying an already-staged import batch by id.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Api - Import] applying already-staged batch {BatchId}")]
    public static partial void LogImportApplyingStagedBatch(this ILogger logger, Guid batchId);

    /// <summary>Logs a generated conflict-resolution override file.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Api - Import] generated conflict-resolution override for {File} ({Origin}) from batch {BatchId} — {Added} rule(s) added")]
    public static partial void LogImportRuleOverrideGenerated(this ILogger logger, string file, string origin, string batchId, int added);

    /// <summary>Logs one request-arrival line (Debug — see docs/logging.md's Request log section).</summary>
    [LoggerMessage(Level = LogLevel.Debug, Message = "{Tag:l} {Id:l} {Method:l} {Url:l}")]
    public static partial void LogRequestStart(this ILogger logger, string tag, string id, string method, string url);

    /// <summary>Logs one request-completion line (Debug — see docs/logging.md's Request log section).</summary>
    [LoggerMessage(Level = LogLevel.Debug, Message = "{Tag:l} {Id:l} {Method:l} {Url:l} → {Status} in {Ms}ms")]
    public static partial void LogRequestEnd(this ILogger logger, string tag, string id, string method, string url, int status, long ms);

    /// <summary>Logs the one clear, actionable message shown when startup database initialisation fails.</summary>
    [LoggerMessage(Level = LogLevel.Critical, Message = "[Server] {Reason} See the exception below for the specific cause.")]
    public static partial void LogStartupDatabaseInitFailed(this ILogger logger, Exception exception, string reason);

    /// <summary>Logs the server stopping, with the running version.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Server] Quotinator v{Version} stopping")]
    public static partial void LogServerStopping(this ILogger logger, string version);

    /// <summary>Logs one "listening on" line per bound Kestrel address.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "[Server] listening on {Address:l}")]
    public static partial void LogListeningOn(this ILogger logger, string address);

    /// <summary>Logs the full closing startup banner as a single entry.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = """

        ##############################
        #     Quotinator ready       #
        ##############################
        Version:        {Version:l}
        Data:           {DataDir:l}
        Database:       {DbPath:l}
                        schema v{SchemaVersion} (data v{DataSchemaVersion}){MigLine:l}
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
        Backups:        {BackupsDir:l}
        DataProtection: {KeysDir:l}
        ------------------------------
        Log level:      {ConfiguredLogLevel:l}
        Log requests:   {LogRequests:l}
        SSL:            {Ssl:l}
        Admin API key:  {AdminApiKey:l}
        ------------------------------
        REST API:       {RestApi:l}
        OpenAPI UI:     {OpenApiUi:l}
        OpenAPI spec:   {OpenApiSpec:l}
        MCP server:     not implemented
        ##############################
        """)]
    public static partial void LogReadyBanner(
        this ILogger logger,
        string version, string dataDir, string dbPath, int schemaVersion, int dataSchemaVersion, string migLine,
        int quoteCount, int sourceCount, int characterCount, int peopleCount, int seriesCount,
        int universeCount, int stageDirectionCount, int soundCueCount, int conversationCount,
        string backupsDir, string keysDir, string configuredLogLevel, string logRequests, string ssl,
        string adminApiKey, string restApi, string openApiUi, string openApiSpec);
}

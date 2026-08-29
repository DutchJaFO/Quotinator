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

    // #349 — reads and destructive actions are logged at different levels, deliberately (developer
    // decision, 2026-08-29). A read is Debug: the status endpoint is designed to be called on every
    // render of the degraded UI, so logging it at Information would bury the lines that matter under
    // its own polling. An action that creates or destroys a restore point is Information, because it
    // is what an operator reconstructing "what happened to my backups" needs to find.

    /// <summary>Logs a paginated read of the backup list (#349). Debug — see the note above.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="tag">The <c>[Api - Name]</c> prefix, built from the endpoint's own <c>WithName</c> constant.</param>
    /// <param name="page">The raw, unparsed <c>page</c> query value.</param>
    /// <param name="pageSize">The raw, unparsed <c>pageSize</c> query value.</param>
    [LoggerMessage(Level = LogLevel.Debug, Message = "{Tag:l} page={Page} pageSize={PageSize}")]
    public static partial void LogBackupListRead(this ILogger logger, string tag, string? page, string? pageSize);

    /// <summary>Logs a read of a backup endpoint that takes no identifying parameter (#349). Debug.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="tag">The <c>[Api - Name]</c> prefix, built from the endpoint's own <c>WithName</c> constant.</param>
    [LoggerMessage(Level = LogLevel.Debug, Message = "{Tag:l} requested")]
    public static partial void LogBackupRead(this ILogger logger, string tag);

    /// <summary>Logs a read of a backup keyed by file name (#349). Debug.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="tag">The <c>[Api - Name]</c> prefix, built from the endpoint's own <c>WithName</c> constant.</param>
    /// <param name="name">The backup file name the caller asked for.</param>
    [LoggerMessage(Level = LogLevel.Debug, Message = "{Tag:l} name={Name:l}")]
    public static partial void LogBackupReadByName(this ILogger logger, string tag, string name);

    /// <summary>
    /// Logs a backup action that created or destroyed a restore point (#349). Information, not Debug:
    /// this is the durable trace an operator reads when a backup they expected is not there, and it is
    /// deliberately visible at the default log level.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="tag">The <c>[Api - Name]</c> prefix, built from the endpoint's own <c>WithName</c> constant.</param>
    /// <param name="action">Past-tense verb for what was done.</param>
    /// <param name="name">The backup file it was done to.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "{Tag:l} {Action:l} {Name:l}")]
    public static partial void LogBackupAction(this ILogger logger, string tag, string action, string name);

    /// <summary>Logs a backup action this application declined to perform, and why (#349).</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="tag">The <c>[Api - Name]</c> prefix, built from the endpoint's own <c>WithName</c> constant.</param>
    /// <param name="reason">The outcome or obstacle that stopped it.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "{Tag:l} refused — {Reason:l}")]
    public static partial void LogBackupRefused(this ILogger logger, string tag, string reason);

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

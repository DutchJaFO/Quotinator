using System.Globalization;
using System.Text.Json.Nodes;
using Serilog;
using Serilog.Events;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;
using Quotinator.Api.Components;
using Quotinator.Api.Endpoints;
using Quotinator.Constants.Api;
using Quotinator.Constants.RateLimiting;
using Quotinator.Constants.Routes;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Engine.Database;
using Quotinator.Engine.Entities;
using Quotinator.Engine.Helpers;
using Quotinator.Engine.Repositories;
using Quotinator.Engine.Services;
using Quotinator.Api.Middleware;
using Quotinator.Api.OpenApi;
using Quotinator.Data.Import;
using Quotinator.Data.Paths;
using Quotinator.Data.Repositories;
using Quotinator.Changelog.Services;
using Quotinator.Converters.BasicJsonArray;
using Quotinator.Converters.Csv;
using Quotinator.Converters.RegexArray;
using Quotinator.Core.Import;
using Quotinator.Core.Services;
using Scalar.AspNetCore;
using Toolbelt.Blazor.Extensions.DependencyInjection;

new QuotinatorDapperConfiguration().Configure();

var builder = WebApplication.CreateBuilder(args);

// Read HA add-on options from /data/options.json when running inside the supervisor.
// The supervisor writes the user's config panel values here; env_vars template rendering
// is not reliably supported for optional options. This is the official HA approach.
var haOptionsPath = "/data/options.json";
var isHa = File.Exists(haOptionsPath);
if (isHa)
{
    var haOptions = System.Text.Json.JsonDocument.Parse(File.ReadAllText(haOptionsPath)).RootElement;
    var haMap = new Dictionary<string, string?>();
    if (haOptions.TryGetProperty("log_level",     out var ll))  haMap["Quotinator:LogLevel"]    = ll.GetString();
    if (haOptions.TryGetProperty("log_requests",  out var lr))  haMap["Quotinator:LogRequests"] = lr.GetRawText();
    if (haOptions.TryGetProperty("ssl",           out var ssl)) haMap["Quotinator:Ssl"]         = ssl.GetRawText();
    if (haOptions.TryGetProperty("certfile",      out var cf))  haMap["Quotinator:SslCertFile"] = $"/ssl/{cf.GetString()}";
    if (haOptions.TryGetProperty("keyfile",       out var kf))  haMap["Quotinator:SslKeyFile"]  = $"/ssl/{kf.GetString()}";
    if (haOptions.TryGetProperty("admin_api_key", out var ak))  haMap["Quotinator:AdminApiKey"] = ak.GetString();
    if (haOptions.TryGetProperty("backup_path",   out var bp))  haMap["Quotinator:BackupPath"]  = bp.GetString();
    builder.Configuration.AddInMemoryCollection(haMap);
}

builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Tags = new HashSet<OpenApiTag>
        {
            new() { Name = ApiTags.System,        Description = "Endpoints for monitoring and verifying the health of the API." },
            new() { Name = ApiTags.Quotes,        Description = "Endpoints for fetching and searching quotes." },
            new() { Name = ApiTags.Admin,         Description = "Administrative endpoints for database maintenance. Require `X-Api-Key` authentication. Protected by a concurrency-1 limiter — only one operation runs at a time; any concurrent request receives `429 Too Many Requests` immediately." },
            new() { Name = ApiTags.Import,        Description = "Endpoints for importing quote data and reviewing/resolving merge conflicts. Write operations require `X-Api-Key` authentication and share the Admin endpoints' concurrency-1 limiter." },
            new() { Name = ApiTags.Conversations, Description = "Endpoints for fetching multi-line conversations (a stage direction and/or sound cue alongside one or more quotes)." },
            new() { Name = ApiTags.MasterData,    Description = "Endpoints for fetching the shared reference data — Sources, Characters, People, Series, and Universes — that quotes and conversations are built from." },
        };

        document.Info = new()
        {
            Title = "Quotinator API",
            Version = "v1",
            Description =
                "A self-hosted quote REST API. Serves real, verified quotes from films, books, " +
                "television, and famous people, from a curated dataset seeded from MIT-licensed sources.\n\n" +
                "**v1 scope:** read-only endpoints for fetching and searching quotes. " +
                "Write endpoints, authentication, and MCP support are planned for v2/v3.\n\n" +
                "**Rate limiting:** sliding-window, 100 requests per minute per IP. Excess requests receive `429 Too Many Requests`.",
            Contact = new() { Name = "GitHub", Url = new Uri("https://github.com/DutchJaFO/Quotinator") }
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
        {
            ["ApiKey"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In   = ParameterLocation.Header,
                Name = "X-Api-Key",
                Description = "Admin API key. Set this once to authenticate all admin endpoint requests."
            }
        };

        return Task.CompletedTask;
    });

    options.AddOperationTransformer<AdminApiKeySecurityTransformer>();
    options.AddOperationTransformer<NumericParameterSchemaTransformer>();
    options.AddOperationTransformer<EnumParameterSchemaTransformer>();
    options.AddSchemaTransformer<ImportModelSchemaTransformer>();
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Sliding window: 100 requests per minute per IP, in 10-second buckets.
    // Generous for normal homelab use; stops runaway scripts and misconfigured consumers.
    options.AddSlidingWindowLimiter(RateLimitPolicies.Api, limiter =>
    {
        limiter.PermitLimit = 100;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.SegmentsPerWindow = 6;
        limiter.QueueLimit = 0;
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });

    // Concurrency-1 for admin endpoints: only one destructive operation runs at a time.
    // QueueLimit = 0 means any concurrent attempt is rejected immediately with 429.
    options.AddConcurrencyLimiter(RateLimitPolicies.Admin, limiter =>
    {
        limiter.PermitLimit = 1;
        limiter.QueueLimit  = 0;
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });

    options.OnRejected = async (context, token) =>
    {
        var localizer = context.HttpContext.RequestServices.GetRequiredService<IApiLocalizer>();
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(
            new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too Many Requests",
                Detail = localizer[ApiMessages.TooManyRequests]
            }, token);
    };
});

// Data directory — configurable so the HA add-on can point this at /data (the supervisor's
// persistent volume) while standalone Docker keeps the default /app/data.
// The HA supervisor sets Quotinator__DataDir via config.yaml env_vars. When that env var
// is absent (e.g. HA caches an older config), fall back to /data if it is already a mounted
// volume (writable directory owned by the HA supervisor), so the database and DataProtection
// keys are always on a persistent volume rather than the ephemeral container filesystem.
static string? HaFallbackDir()
{
    const string haData = "/data";
    try { return Directory.Exists(haData) ? haData : null; }
    catch { return null; }
}
var dataDir = builder.Configuration["Quotinator:DataDir"]
    ?? HaFallbackDir()
    ?? Path.Combine(AppContext.BaseDirectory, "data");
Directory.CreateDirectory(dataDir);

// Duplicate-resolution policy from config — lowest-priority tier; a manifest's own
// duplicateResolution section overrides this when present. Quotinator:DefaultConflictPolicy is a
// flat key (env Quotinator__DefaultConflictPolicy) — the 5 nested per-type keys below keep their
// existing paths, minus the now-redundant "Default" sibling that used to live under
// Quotinator:DuplicateResolution. Parsing itself lives in ConflictPolicyParser (Quotinator.Data)
// so it's unit-testable outside these top-level statements.
var configPolicy = new ManifestPolicy(
    Default:      ConflictPolicyParser.Parse(builder.Configuration["Quotinator:DefaultConflictPolicy"]),
    Quotes:       ConflictPolicyParser.ParseNullable(builder.Configuration["Quotinator:DuplicateResolution:Quotes"]),
    Sources:      ConflictPolicyParser.ParseNullable(builder.Configuration["Quotinator:DuplicateResolution:Sources"]),
    Characters:   ConflictPolicyParser.ParseNullable(builder.Configuration["Quotinator:DuplicateResolution:Characters"]),
    People:       ConflictPolicyParser.ParseNullable(builder.Configuration["Quotinator:DuplicateResolution:People"]),
    Translations: ConflictPolicyParser.ParseNullable(builder.Configuration["Quotinator:DuplicateResolution:Translations"]));

var createMissingManifest  = builder.Configuration.GetValue("Quotinator:CreateMissingManifest", true);
var includeDefaultSources  = builder.Configuration.GetValue("Quotinator:IncludeDefaultSources", true);

// Auto-update: whether the app checks manifest downloadUrl/github entries for a fresher copy at
// all (master switch — false means pure offline mode, no network calls ever), and how long a
// downloaded copy is considered fresh before the next check re-verifies it.
var autoUpdateSources        = builder.Configuration.GetValue("Quotinator:AutoUpdateSources", true);
var sourceUpdateIntervalHours = builder.Configuration.GetValue("Quotinator:SourceUpdateIntervalHours", 24);

// Bundled sources are always read from the Docker image (AppContext.BaseDirectory/data/sources/).
// No file copy to the persistent volume is needed — only the database and DataProtection keys
// need to be on a writable, persistent path.
var bundledSourcesDir = Path.Combine(AppContext.BaseDirectory, "data", DataPaths.SourcesFolder);

// User imports: optional directory in the data volume. Create it so users can drop files in.
// Quotinator:ImportsPath overrides the default location when set.
var importsDir = builder.Configuration["Quotinator:ImportsPath"] is { Length: > 0 } customImportsPath
    ? customImportsPath
    : Path.Combine(dataDir, DataPaths.ImportsFolder);

// Auto-update download caches — always under the persistent data volume, never the read-only
// bundled image path, so both are writable in every deployment shape including the HA add-on.
// "Internal" is the default cache for bundled-manifest entries; "external" for user-imports entries.
var internalDownloadDir = Path.Combine(dataDir, DataPaths.SourcesFolder, DataPaths.DownloadedSourcesFolder);
var externalDownloadDir = Path.Combine(dataDir, DataPaths.ImportsFolder, DataPaths.DownloadedSourcesFolder);

// Persist DataProtection keys to a subdirectory of the data volume so antiforgery tokens
// and Blazor circuit descriptors survive container restarts and add-on updates.
var keysDir = Path.Combine(dataDir, DataPaths.DataProtectionFolder);
Directory.CreateDirectory(keysDir);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysDir));

// Trust X-Forwarded-For / X-Forwarded-Proto from upstream proxies (HA ingress, reverse proxies).
// This makes Request.IsHttps correct when Quotinator sits behind an HTTPS proxy, which is
// required for Secure cookie flags and the Blazor circuit antiforgery handshake to work.
// Clearing KnownNetworks/KnownProxies is intentional: homelab deployments use trusted LAN proxies.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Optional HTTPS via Kestrel — for direct-access deployments without a terminating proxy.
// When running in a container, port binding is handled here instead of ASPNETCORE_HTTP_PORTS
// so that HTTPS on 8080 and HTTP on 8099 (HA ingress) do not conflict.
var sslEnabled  = builder.Configuration.GetValue<bool>("Quotinator:Ssl");
var sslCertFile = builder.Configuration["Quotinator:SslCertFile"] ?? string.Empty;
var sslKeyFile  = builder.Configuration["Quotinator:SslKeyFile"]  ?? string.Empty;
var isContainer = string.Equals(
    Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true",
    StringComparison.OrdinalIgnoreCase);

if (isContainer)
{
    builder.WebHost.ConfigureKestrel(kestrel =>
    {
        // Port 8099 is always plain HTTP — used by the HA ingress (internal traffic only).
        kestrel.ListenAnyIP(8099);

        if (sslEnabled && File.Exists(sslCertFile) && File.Exists(sslKeyFile))
            kestrel.ListenAnyIP(8080, lo => lo.UseHttps(sslCertFile, sslKeyFile));
        else
            kestrel.ListenAnyIP(8080);
    });
}

// Omit null properties from all JSON responses — verified against System.Text.Json docs:
// JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull skips any
// property whose value is null at serialization time, application-wide (not merely a formatting
// choice for one endpoint).
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull);

builder.Services.AddExceptionHandler<BadRequestExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<IVersionService, VersionService>();
builder.Services.AddSingleton<IChangelogService>(sp =>
    new ChangelogService(
        Path.Combine(AppContext.BaseDirectory, "resources"),
        sp.GetRequiredService<ILogger<ChangelogService>>()));

var dbPath     = Path.Combine(dataDir, DataPaths.DatabaseFile);
var backupsDir = builder.Configuration["Quotinator:BackupPath"] is { Length: > 0 } customBackupPath
    ? customBackupPath
    : Path.Combine(dataDir, DataPaths.BackupsFolder);
var dbOptions         = new DatabaseOptions { DbPath = dbPath, BackupsPath = backupsDir };
var connectionFactory = new SqliteConnectionFactory(dbPath);
builder.Services.AddSingleton<IDbConnectionFactory>(_ => connectionFactory);
builder.Services.AddTransient<IUnitOfWork>(sp =>
    new SqliteUnitOfWork(sp.GetRequiredService<IDbConnectionFactory>()));
// InitiatorContext implements both interfaces over the same AsyncLocal-backed instance, so
// SqliteRepository<T>'s existing ICallerContext.Agent reads are unaffected by IInitiatorContext's
// introduction — same singleton, same per-async-context isolation, just a richer surface for callers
// that need InitiatedByType/InitiatedById too.
builder.Services.AddSingleton<InitiatorContext>();
builder.Services.AddSingleton<ICallerContext>(sp => sp.GetRequiredService<InitiatorContext>());
builder.Services.AddSingleton<IInitiatorContext>(sp => sp.GetRequiredService<InitiatorContext>());
builder.Services.AddSingleton<ISystemAuditWriter, SystemAuditWriter>();
builder.Services.AddSingleton<ISystemAuditReader, SystemAuditReader>();
builder.Services.AddSingleton<ISystemChangeLogWriter, SystemChangeLogWriter>();
builder.Services.AddSingleton<ISystemChangeLogReader, SystemChangeLogReader>();
builder.Services.AddSingleton<ISystemImportActionWriter, SystemImportActionWriter>();
builder.Services.AddSingleton<ISystemImportActionReader, SystemImportActionReader>();
builder.Services.AddSingleton<IImportActionCoordinator, ImportActionResolutionCoordinator>();
builder.Services.AddSingleton<IImportActionService, SqliteImportActionService>();

// #59: restorable-repository access for Quote/Source/Character/Person, needed only by batch-undo
// (reversal) — nothing else in the app soft-deletes these tables today. Fully generic, already
// tested against a synthetic fixture in Quotinator.Data.Tests; no new repository code required.
builder.Services.AddSingleton<IRestorableRepository<QuoteEntity>, SqliteRestorableRepository<QuoteEntity>>();
builder.Services.AddSingleton<IRestorableRepository<Source>, SqliteRestorableRepository<Source>>();
builder.Services.AddSingleton<IRestorableRepository<Character>, SqliteRestorableRepository<Character>>();
builder.Services.AddSingleton<IRestorableRepository<Person>, SqliteRestorableRepository<Person>>();

// #68: same rationale as above, for Conversation/StageDirection/SoundCue — needed by
// SqliteImportActionService's stale-Add-target hard-delete and batch-reversal soft-delete/restore.
// ConversationLines/StageDirectionTranslations/SoundCueTranslations are detail rows (like
// QuoteGenres/QuoteTranslations) and never get their own repository.
builder.Services.AddSingleton<IRestorableRepository<ConversationEntity>, SqliteRestorableRepository<ConversationEntity>>();
builder.Services.AddSingleton<IRestorableRepository<StageDirectionEntity>, SqliteRestorableRepository<StageDirectionEntity>>();
builder.Services.AddSingleton<IRestorableRepository<SoundCueEntity>, SqliteRestorableRepository<SoundCueEntity>>();

// #193: listable-repository capability, needed by #184-#189's masterdata list endpoints.
// SeriesEntity/UniverseEntity get their first repository of any kind here; the other four resolve to
// their existing IRestorableRepository<T> singleton above — a second interface binding onto the same
// object (SqliteRestorableRepository<T> already implements IListableRepository<T> transitively, since
// it extends SqliteRepository<T>), not a second instance.
builder.Services.AddSingleton<IListableRepository<SeriesEntity>, SqliteRepository<SeriesEntity>>();
builder.Services.AddSingleton<IListableRepository<UniverseEntity>, SqliteRepository<UniverseEntity>>();
builder.Services.AddSingleton<IListableRepository<Source>>(sp => (IListableRepository<Source>)sp.GetRequiredService<IRestorableRepository<Source>>());
builder.Services.AddSingleton<IListableRepository<Character>>(sp => (IListableRepository<Character>)sp.GetRequiredService<IRestorableRepository<Character>>());
builder.Services.AddSingleton<IListableRepository<Person>>(sp => (IListableRepository<Person>)sp.GetRequiredService<IRestorableRepository<Person>>());
builder.Services.AddSingleton<IListableRepository<ConversationEntity>>(sp => (IListableRepository<ConversationEntity>)sp.GetRequiredService<IRestorableRepository<ConversationEntity>>());

// #184: resolves a Source's SeriesId to its Series' (Id, Name) — the generic IListableRepository<T>/
// IRepository<T> above cannot express this join (single-table SELECT * only).
builder.Services.AddSingleton<ISourceSeriesReferenceReader, SourceSeriesReferenceReader>();

// #185: resolves a Character's linked Sources (via CharacterSources, #179) to their (Id, Title) —
// same "generic repository cannot express a join" reasoning as ISourceSeriesReferenceReader above.
builder.Services.AddSingleton<ICharacterSourceLinkReader, CharacterSourceLinkReader>();

// #187: resolves a Series' UniverseId to its Universe's (Id, Name) — same "generic repository cannot
// express a join" reasoning as ISourceSeriesReferenceReader above.
builder.Services.AddSingleton<ISeriesUniverseReferenceReader, SeriesUniverseReferenceReader>();

// Seed batches are resolved lazily inside the IDatabaseInitializer factory below, rather than
// eagerly before builder.Build(), so manifest planning (including auto-create) logs through the
// real Serilog pipeline at the same point in startup as the rest of seeding — not through a
// separate bootstrap console logger that runs before the "Quotinator starting" banner.
builder.Services.AddSingleton<IManifestSeedPlanner, ManifestSeedPlanner>();
builder.Services.AddSingleton<IImportBatchRepository, SqliteImportBatchRepository>();

// 5 s timeout: a slow/unreachable upstream must never block startup, reseed, or reset for longer
// than a brief, bounded check — the updater always falls back to the existing cached/local file.
builder.Services.AddHttpClient(SourceCacheUpdater.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(5));

// Converters are stateless, hardcoded per source — no DI registration needed for the individual
// plugin instances themselves (CLAUDE.md's DI policy: bare `new` is permitted for a computed value
// assembled before a factory closure, same shape already used for SourceCacheOptions itself).
var quoteSourceConverters = new IQuoteSourceConverter[]
{
    new RegexArrayConverter(),
    new BasicJsonArrayConverter(),
    new CsvQuoteConverter(),
}.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

// Real canonical-schema validation needs Quotinator.Core's SourceQuote, but Quotinator.Data (home of
// SourceCacheUpdater) must not depend on Quotinator.Core — so the validator is built here, at the
// composition root, and injected as a plain delegate.
Func<string, bool> validateCanonicalSchema = json => SourceQuoteFileReader.TryParse(json, out _);

builder.Services.AddSingleton<ISourceCacheUpdater>(sp => new SourceCacheUpdater(
    sp.GetRequiredService<IHttpClientFactory>(),
    new SourceCacheOptions(internalDownloadDir, externalDownloadDir, sourceUpdateIntervalHours,
        quoteSourceConverters, validateCanonicalSchema),
    sp.GetRequiredService<ILogger<SourceCacheUpdater>>()));

builder.Services.AddSingleton<IDatabaseInitializer>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<Program>>();
    LegacyConfigWarnings.WarnIfDataPathStillSet(builder.Configuration["Quotinator:DataPath"], logger);

    var seedBatches = SeedBatchesBuilder.Build(
        bundledSourcesDir, importsDir, configPolicy, includeDefaultSources, createMissingManifest,
        sp.GetRequiredService<IManifestSeedPlanner>(), logger);

    return new QuotinatorDatabaseInitializer(
        connectionFactory, dbOptions, QuotinatorMigrations.All, seedBatches,
        sp.GetRequiredService<IImportBatchRepository>(),
        sp.GetRequiredService<IImportActionCoordinator>(),
        sp.GetRequiredService<IImportActionService>(),
        sp.GetRequiredService<ISystemAuditWriter>(),
        sp.GetRequiredService<ICallerContext>(),
        sp.GetRequiredService<ILogger<DatabaseInitializer>>(),
        sp.GetRequiredService<ISourceCacheUpdater>(),
        autoUpdateSources,
        QuotinatorMigrations.Baseline);
});
builder.Services.AddSingleton<IQuoteService>(_ => new Quotinator.Engine.Services.SqliteQuoteService(connectionFactory));
builder.Services.AddSingleton<Quotinator.Engine.Services.IQuoteImportService>(sp => new Quotinator.Engine.Services.SqliteQuoteImportService(
    connectionFactory,
    sp.GetRequiredService<IImportBatchRepository>(),
    sp.GetRequiredService<IImportActionCoordinator>(),
    sp.GetRequiredService<IImportActionService>(),
    sp.GetRequiredService<ISystemImportActionReader>(),
    quoteSourceConverters,
    configPolicy));
builder.Services.AddSingleton<RequestLoggingMiddleware>();
builder.Services.AddSingleton<IApiLocalizer>(
    new ApiLocalizer(Path.Combine(AppContext.BaseDirectory, "i18ntext")));
builder.Services.AddI18nText(options =>
{
    // Use ASP.NET Core's culture (set from the .AspNetCore.Culture cookie by
    // RequestLocalizationMiddleware) instead of the default JS navigator.language detection.
    // This ensures Interactive Server components respect the cookie-selected language.
    options.GetInitialLanguageAsync = (_, _) =>
        ValueTask.FromResult(CultureInfo.CurrentUICulture.Name);
});

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supported = new[] { "en-GB", "de", "nl" };
    options.DefaultRequestCulture = new RequestCulture("en-GB");
    options.AddSupportedCultures(supported);
    options.AddSupportedUICultures(supported);
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Map HA log level names (trace/debug/info/notice/warning/error/fatal) to Serilog levels.
var haLogLevel = builder.Configuration["Quotinator:LogLevel"] ?? "info";
var serilogLevel = haLogLevel.ToLowerInvariant() switch
{
    "trace"   => LogEventLevel.Verbose,
    "debug"   => LogEventLevel.Debug,
    "notice"  => LogEventLevel.Information,
    "info"    => LogEventLevel.Information,
    "warning" => LogEventLevel.Warning,
    "error"   => LogEventLevel.Error,
    "fatal"   => LogEventLevel.Fatal,
    _         => LogEventLevel.Information
};

// Configured in code — not via ReadFrom.Configuration — because the HA supervisor container
// denies directory listing on /app, which Serilog.Settings.Configuration scans for sink DLLs.
builder.Host.UseSerilog((ctx, _, config) =>
{
    var isDev = ctx.HostingEnvironment.IsDevelopment();
    var template = isDev
        ? "{Timestamp:HH:mm:ss} {Level:u3}: {SourceContext}[{EventId:0}] {Message}{NewLine}{Exception}"
        : "{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}: {SourceContext}[{EventId:0}] {Message}{NewLine}{Exception}";

    config
        .MinimumLevel.Is(serilogLevel)
        .MinimumLevel.Override("Microsoft.AspNetCore",             LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.AspNetCore.DataProtection", LogEventLevel.Error)
        .MinimumLevel.Override("Microsoft.Hosting.Lifetime",      LogEventLevel.Information)
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate: template);

    if (isDev)
        config.WriteTo.Debug();
});

var app = builder.Build();

var dbInitializer  = app.Services.GetRequiredService<IDatabaseInitializer>();
var versionService = app.Services.GetRequiredService<IVersionService>();
var logRequests    = app.Configuration.GetValue<bool>("Quotinator:LogRequests");
var adminKeyConfigured = !string.IsNullOrEmpty(app.Configuration["Quotinator:AdminApiKey"]);

// StartupSummaryLogger is a one-shot startup utility, not a general-purpose service;
// instantiated directly rather than registered with DI.
var startupLog = new Quotinator.Api.Startup.StartupSummaryLogger(
    app.Services.GetRequiredService<ILogger<Quotinator.Api.Startup.StartupSummaryLogger>>(),
    dbInitializer, versionService,
    dataDir, dbPath, backupsDir, keysDir,
    haLogLevel, logRequests, sslEnabled, adminKeyConfigured, isHa);

startupLog.LogStarting();
await dbInitializer.InitialiseAsync();

var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();

// Closing banner fires after Kestrel binds so bound addresses are available.
lifetime.ApplicationStarted.Register(() =>
{
    var addresses = (app.Services
        .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
        .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()
        ?.Addresses ?? []).ToList();
    startupLog.LogReady(addresses);
});

var logger = app.Services.GetRequiredService<ILogger<Program>>();
lifetime.ApplicationStopping.Register(() =>
    logger.LogInformation("[Server] Quotinator v{Version} stopping", versionService.Version));

// Must be first so all subsequent middleware sees the correct scheme and client IP.
app.UseForwardedHeaders();

// The HA supervisor sets X-Ingress-Path to the ingress prefix (e.g. /api/hassio_ingress/TOKEN).
// Applying it as PathBase makes <base href> render correctly so all relative asset URLs
// (blazor.web.js, CSS, etc.) resolve through the ingress proxy rather than HA's own server.
app.Use(async (context, next) =>
{
    if (context.Request.Headers.TryGetValue("X-Ingress-Path", out var ingressPath)
        && !string.IsNullOrEmpty(ingressPath))
    {
        context.Request.PathBase = new PathString(ingressPath.ToString());
    }
    await next();
});

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRequestLocalization();
app.UseRateLimiter();

// Populate ICallerContext.Agent from the User-Agent header for audit trail entries.
// Only the value is read — the header name is not logged or stored anywhere.
app.Use(async (context, next) =>
{
    var callerContext = context.RequestServices.GetRequiredService<ICallerContext>();
    callerContext.Agent = context.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;
    await next();
});

// Optional request logging — logs every endpoint call as two lines (start + end) with a
// per-request correlation ID. Off by default. Enable with log_requests: true in the add-on
// config (or Quotinator__LogRequests=true). All endpoints are logged; header values are never
// captured (X-Api-Key, Authorization, Cookie must not appear in logs).
if (logRequests)
    app.UseMiddleware<RequestLoggingMiddleware>();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseAntiforgery();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet(ApiRoutes.Health, () => Results.Ok(new { status = "healthy" }))
   .WithName("Health")
   .WithTags(ApiTags.System)
   .WithSummary("Health check")
   .WithDescription("Returns the current health status of the API. Use this endpoint to verify the service is running.");

app.MapGet(ApiRoutes.Version, (IVersionService vs, IWebHostEnvironment env, IDatabaseInitializer db) =>
    Results.Ok(new
    {
        version     = vs.Version,
        environment = env.EnvironmentName,
        database    = new
        {
            schemaVersion = db.SchemaVersion,
            quotes        = db.QuoteCount,
            sources       = db.SourceCount,
            characters    = db.CharacterCount,
            people        = db.PeopleCount
        }
    }))
   .WithName("Version")
   .WithTags(ApiTags.System)
   .WithSummary("API version")
   .WithDescription("Returns the running version, environment, and database schema version with row counts.");

app.MapQuoteEndpoints();
app.MapAdminEndpoints();
app.MapImportEndpoints();
app.MapConversationEndpoints();
app.MapSourceEndpoints();
app.MapCharacterEndpoints();
app.MapPersonEndpoints();
app.MapSeriesEndpoints();
app.MapUniverseEndpoints();

// Sets or clears the UI language cookie and redirects back. LocalRedirect prevents open-redirect attacks.
// Empty culture = auto-detect mode: deletes the cookie so Accept-Language takes over.
// Non-empty culture: sets the cookie (c={culture}|uic={culture}) read by CookieRequestCultureProvider.
app.MapGet(ApiRoutes.CultureSet, (string? culture, string redirectUri, HttpContext context) =>
{
    if (string.IsNullOrEmpty(culture))
    {
        context.Response.Cookies.Delete(CookieRequestCultureProvider.DefaultCookieName,
            new CookieOptions { SameSite = SameSiteMode.Lax, Secure = context.Request.IsHttps });
    }
    else
    {
        context.Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture, culture)),
            new CookieOptions { MaxAge = TimeSpan.FromDays(365), IsEssential = true, SameSite = SameSiteMode.Lax, Secure = context.Request.IsHttps });
    }
    return TypedResults.LocalRedirect(redirectUri);
})
.ExcludeFromDescription();

app.Run();

// Exposes Program to WebApplicationFactory<Program> in the test project.
public partial class Program { }

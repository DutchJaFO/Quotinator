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
using Quotinator.Data.Enums;
using Quotinator.Core.Database;
using Quotinator.Core.Entities;
using Quotinator.Core.Helpers;
using Quotinator.Core.Queries;
using Quotinator.Core.Repositories;
using Quotinator.Core.Services;
using Quotinator.Api.Middleware;
using Quotinator.Api.OpenApi;
using Quotinator.Api.Services;
using Quotinator.Data.Import;
using Quotinator.Data.Paths;
using Quotinator.Data.Queries;
using Quotinator.Data.Repositories;
using Quotinator.Changelog.Services;
using Quotinator.Converters.BasicJsonArray;
using Quotinator.Converters.Csv;
using Quotinator.Converters.RegexArray;
using Quotinator.Core.Import;
using Quotinator.Api.Logging;
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
var sourceRefreshTimeoutSeconds = builder.Configuration.GetValue<int?>("Quotinator:SourceRefreshTimeoutSeconds")
    ?? SourceCacheUpdater.DefaultHttpTimeoutSeconds;

// #278: default expiry applied to a notification written without an explicit expiresAt. Read once
// here (not per-request, unlike AdminAuditExportMaxRows) since it's a NotificationWriter constructor
// dependency, not a per-call local — same pattern as sourceRefreshTimeoutSeconds above.
var notificationDefaultExpiryHours = builder.Configuration.GetValue<int?>("Quotinator:NotificationDefaultExpiryHours")
    ?? QueryParamDefaults.NotificationDefaultExpiryHours;

// #249: once a seeded batch reaches zero pending actions, its Import_Action (conflict-resolution)
// rows have served their purpose and are purged automatically — separate settings per origin so a
// developer investigating one specific source (bundled or user-imports) can temporarily retain that
// origin's resolution history without affecting the other.
var autoPurgeBundledImportActions = builder.Configuration.GetValue("Quotinator:AutoPurgeBundledImportActions", true);
var autoPurgeUserImportActions    = builder.Configuration.GetValue("Quotinator:AutoPurgeUserImportActions", true);

// Unicode-aware LIKE-style matching (issue #222) — opt-in, off by default until validated against
// real-world non-ASCII search traffic. See docs/milestones/maintenance-milestone-v1.8.0/
// 222-unicode-like-matching-plan.md for why this isn't unconditional.
var unicodeAwareSearch = builder.Configuration.GetValue("Quotinator:UnicodeAwareSearch", false);

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
// #309: bundled changelog files read from the Docker image (AppContext.BaseDirectory/data/changelog/),
// mirroring bundledSourcesDir above — no longer compiled resources, per ADR 005's revision.
var bundledChangelogDir = Path.Combine(AppContext.BaseDirectory, "data", DataPaths.ChangelogFolder);
builder.Services.AddSingleton<IChangelogService>(sp =>
    new ChangelogService(
        bundledChangelogDir,
        sp.GetRequiredService<ILogger<ChangelogService>>()));

var dbPath     = Path.Combine(dataDir, DataPaths.DatabaseFile);
var backupsDir = builder.Configuration["Quotinator:BackupPath"] is { Length: > 0 } customBackupPath
    ? customBackupPath
    : Path.Combine(dataDir, DataPaths.BackupsFolder);
var maxBackupStorageGb = builder.Configuration.GetValue("Quotinator:MaxBackupStorageGb", 1);
var dbOptions          = new DatabaseOptions { DbPath = dbPath, BackupsPath = backupsDir, MaxBackupStorageGb = maxBackupStorageGb };
// useMemoryTempStore: true — see SqliteConnectionFactory.cs's own comment for the #294 incident this
// opts into working around. Safe here because Quotinator's own dataset (hundreds to low-thousands of
// quotes) makes the resulting RAM cost negligible; Quotinator.Data itself stays unopinionated and
// defaults to false since it doesn't know a future consumer's dataset size.
var connectionFactory  = new SqliteConnectionFactory(dbPath, useMemoryTempStore: true);
builder.Services.AddSingleton<IDiskSpaceProvider, DiskSpaceProvider>();
builder.Services.AddSingleton<IDbConnectionFactory>(_ => connectionFactory);

// #309: separate, in-memory database for changelog content (ADR 018) — no relational or transactional
// coupling to domain data, so it lives outside quotinatordata.db entirely. Shared-cache mode
// (mode=memory&cache=shared) lets every separately-opened connection see the same in-memory database;
// ChangelogConnectionKeepAlive (resolved eagerly below, after app.Build()) holds one connection open
// for the app's lifetime, since a shared-cache in-memory database is destroyed the moment its last
// open connection closes. useMemoryTempStore: true for the same #294 reason as the main database.
var changelogConnectionFactory = new SqliteConnectionFactory(
    "file:quotinatorchangelog?mode=memory&cache=shared", useMemoryTempStore: true);
builder.Services.AddKeyedSingleton<IDbConnectionFactory>(
    DatabaseConnectionKeys.Changelog, (_, _) => changelogConnectionFactory);
builder.Services.AddSingleton<ChangelogConnectionKeepAlive>();
builder.Services.AddSingleton<ChangelogDatabaseInitializer>();
builder.Services.AddSingleton<ChangelogRepository>();
builder.Services.AddSingleton<ChangelogSystemContentImporter>();
// JoinQueryRepository/IJoinStrategy per ADR 017. Factory overload (not the bare AddSingleton<
// JoinQueryRepository<T>>() every other join query below uses) — those all resolve the main
// database's own unkeyed IDbConnectionFactory; this one must resolve the changelog database's keyed
// factory instead, which the container can't supply implicitly at registration time.
builder.Services.AddSingleton<IJoinStrategy<ChangelogLineRow>, ChangelogWithLinesStrategy>();
builder.Services.AddSingleton(sp => new JoinQueryRepository<ChangelogLineRow>(
    sp.GetRequiredKeyedService<IDbConnectionFactory>(DatabaseConnectionKeys.Changelog),
    sp.GetRequiredService<IJoinStrategy<ChangelogLineRow>>()));
builder.Services.AddSingleton<IChangelogReader, ChangelogReader>();
builder.Services.AddTransient<IUnitOfWork>(sp =>
    new SqliteUnitOfWork(sp.GetRequiredService<IDbConnectionFactory>()));
// InitiatorContext implements both interfaces over the same AsyncLocal-backed instance, so
// SqliteRepository<T>'s existing ICallerContext.Agent reads are unaffected by IInitiatorContext's
// introduction — same singleton, same per-async-context isolation, just a richer surface for callers
// that need InitiatedByType/InitiatedById too.
builder.Services.AddSingleton<InitiatorContext>();
builder.Services.AddSingleton<ICallerContext>(sp => sp.GetRequiredService<InitiatorContext>());
builder.Services.AddSingleton<IInitiatorContext>(sp => sp.GetRequiredService<InitiatorContext>());
builder.Services.AddSingleton<IAuditEntryWriter, AuditEntryWriter>();
builder.Services.AddSingleton<IAuditEntryReader, AuditEntryReader>();
builder.Services.AddSingleton<IChangeWriter, ChangeWriter>();
builder.Services.AddSingleton<IChangeReader, ChangeReader>();
builder.Services.AddSingleton<IImportActionWriter, ImportActionWriter>();
builder.Services.AddSingleton<IImportActionReader, ImportActionReader>();
builder.Services.AddSingleton<ISourceFileOverrideRegistry, SourceFileOverrideRegistry>();
builder.Services.AddSingleton<IFileResourceRepository, SqliteFileResourceRepository>();
builder.Services.AddSingleton<IImportActionCoordinator, ImportActionResolutionCoordinator>();
builder.Services.AddSingleton<IImportActionService, SqliteImportActionService>();
builder.Services.AddSingleton<INotificationReader, NotificationReader>();
// Factory overload — the container can't supply notificationDefaultExpiryHours (a computed config
// value) at registration time, matching this project's documented DI exception.
builder.Services.AddSingleton<INotificationWriter>(sp =>
    new NotificationWriter(sp.GetRequiredService<IDbConnectionFactory>(), notificationDefaultExpiryHours));
builder.Services.AddSingleton<INotificationActionExecutor, NotificationActionExecutor>();
builder.Services.AddSingleton<IAppVersionTracker, AppVersionTracker>();

// #59: restorable-repository access for Quote/Source/Character/Person, needed only by batch-undo
// (reversal) — nothing else in the app soft-deletes these tables today. Fully generic, already
// tested against a synthetic fixture in Quotinator.Data.Tests; no new repository code required.
builder.Services.AddSingleton<IRestorableRepository<QuoteEntity>, SqliteRestorableRepository<QuoteEntity>>();
builder.Services.AddSingleton<IRestorableRepository<SourceEntity>, SqliteRestorableRepository<SourceEntity>>();
builder.Services.AddSingleton<IRestorableRepository<CharacterEntity>, SqliteRestorableRepository<CharacterEntity>>();
builder.Services.AddSingleton<IRestorableRepository<PersonEntity>, SqliteRestorableRepository<PersonEntity>>();

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
builder.Services.AddSingleton<IListableRepository<SourceEntity>>(sp => (IListableRepository<SourceEntity>)sp.GetRequiredService<IRestorableRepository<SourceEntity>>());
builder.Services.AddSingleton<IListableRepository<CharacterEntity>>(sp => (IListableRepository<CharacterEntity>)sp.GetRequiredService<IRestorableRepository<CharacterEntity>>());
builder.Services.AddSingleton<IListableRepository<PersonEntity>>(sp => (IListableRepository<PersonEntity>)sp.GetRequiredService<IRestorableRepository<PersonEntity>>());
builder.Services.AddSingleton<IListableRepository<ConversationEntity>>(sp => (IListableRepository<ConversationEntity>)sp.GetRequiredService<IRestorableRepository<ConversationEntity>>());

// #204: StageDirectionEntity was left out of #193's original six-entity scope. Same "second interface
// binding onto the existing IRestorableRepository<T> singleton, not a second instance" reasoning as the
// four bindings immediately above.
builder.Services.AddSingleton<IListableRepository<StageDirectionEntity>>(sp => (IListableRepository<StageDirectionEntity>)sp.GetRequiredService<IRestorableRepository<StageDirectionEntity>>());

// #205: SoundCueEntity was left out of #193's original six-entity scope, the same gap #204 closed for
// StageDirectionEntity. Same "second interface binding onto the existing IRestorableRepository<T>
// singleton, not a second instance" reasoning.
builder.Services.AddSingleton<IListableRepository<SoundCueEntity>>(sp => (IListableRepository<SoundCueEntity>)sp.GetRequiredService<IRestorableRepository<SoundCueEntity>>());

// #184/#284: resolves a Source's SeriesId to its Series' (Id, Name). SQL execution goes through
// JoinQueryRepository/IJoinStrategy per ADR 017 — a join the generic IListableRepository<T>/
// IRepository<T> above cannot express (single-table SELECT * only), even though adopting the pattern
// here doesn't unlock new capability over a hand-rolled query; see ADR 017 for why that's still the
// right call.
builder.Services.AddSingleton<IJoinStrategy<SeriesReferenceRow>, SourceSeriesReferenceStrategy>();
builder.Services.AddSingleton<JoinQueryRepository<SeriesReferenceRow>>();
builder.Services.AddSingleton<IJoinStrategy<SourceSeriesReferenceRow>, SourceSeriesReferencesBatchStrategy>();
builder.Services.AddSingleton<JoinQueryRepository<SourceSeriesReferenceRow>>();
builder.Services.AddSingleton<ISourceSeriesReferenceReader, SourceSeriesReferenceReader>();

// #185/#284: resolves a Character's linked Sources (via CharacterSources, #179) to their (Id, Title) —
// same ADR 017 reasoning as ISourceSeriesReferenceReader above.
builder.Services.AddSingleton<IJoinStrategy<SourceRow>, CharacterSourceReferenceStrategy>();
builder.Services.AddSingleton<JoinQueryRepository<SourceRow>>();
builder.Services.AddSingleton<IJoinStrategy<LinkRow>, CharacterSourceReferencesBatchStrategy>();
builder.Services.AddSingleton<JoinQueryRepository<LinkRow>>();
builder.Services.AddSingleton<ICharacterSourceLinkReader, CharacterSourceLinkReader>();

// #187/#284: resolves a Series' UniverseId to its Universe's (Id, Name) — same ADR 017 reasoning as
// ISourceSeriesReferenceReader above.
builder.Services.AddSingleton<IJoinStrategy<UniverseReferenceRow>, SeriesUniverseReferenceStrategy>();
builder.Services.AddSingleton<JoinQueryRepository<UniverseReferenceRow>>();
builder.Services.AddSingleton<IJoinStrategy<SeriesUniverseReferenceRow>, SeriesUniverseReferencesBatchStrategy>();
builder.Services.AddSingleton<JoinQueryRepository<SeriesUniverseReferenceRow>>();
builder.Services.AddSingleton<ISeriesUniverseReferenceReader, SeriesUniverseReferenceReader>();

// #189: resolves each Conversation's active line count via ConversationLines. Deliberately stays on
// a raw connection, not JoinQueryRepository/IJoinStrategy — ADR 017's one documented exemption, since
// this read's QueryAsync<dynamic> works around two real Dapper/SQLite bugs that IJoinStrategy<TResult>'s
// concrete-TResult requirement can't accommodate (see the reader's own code comment for the two bugs).
builder.Services.AddSingleton<IConversationLineCountReader, ConversationLineCountReader>();

// #192: resolves a Series/Universe name to its id — the resolveIdByName delegate #196's
// EntityFilterParsing.ResolveAsync needs for the quote read path's Series/Universe filters.
builder.Services.AddSingleton<ISeriesNameResolver, SeriesNameResolver>();
builder.Services.AddSingleton<IUniverseNameResolver, UniverseNameResolver>();

// Seed batches are resolved lazily inside the IDatabaseInitializer factory below, rather than
// eagerly before builder.Build(), so manifest planning (including auto-create) logs through the
// real Serilog pipeline at the same point in startup as the rest of seeding — not through a
// separate bootstrap console logger that runs before the "Quotinator starting" banner.
builder.Services.AddSingleton<IManifestSeedPlanner, ManifestSeedPlanner>();
builder.Services.AddSingleton<IImportBatchRepository, SqliteImportBatchRepository>();

// Overridable via Quotinator:SourceRefreshTimeoutSeconds — see SourceCacheUpdater.DefaultHttpTimeoutSeconds
// for why 30 s is the default.
builder.Services.AddHttpClient(SourceCacheUpdater.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(sourceRefreshTimeoutSeconds));

// Converters are stateless, hardcoded per source — no DI registration needed for the individual
// plugin instances themselves (CLAUDE.md's DI policy: bare `new` is permitted for a computed value
// assembled before a factory closure, same shape already used for SourceCacheOptions itself).
var quoteSourceConverters = new IQuoteSourceConverter[]
{
    new RegexArrayConverter(),
    new BasicJsonArrayConverter(),
    new CsvQuoteConverter(),
}.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

// Real canonical-schema validation needs Quotinator.Core's SourceQuoteDto, but Quotinator.Data (home of
// SourceCacheUpdater) must not depend on Quotinator.Core — so the validator is built here, at the
// composition root, and injected as a plain delegate.
static bool ValidateCanonicalSchema(string json) => SourceQuoteFileReader.TryParse(json, out _);

builder.Services.AddSingleton<ISourceCacheUpdater>(sp => new SourceCacheUpdater(
    sp.GetRequiredService<IHttpClientFactory>(),
    new SourceCacheOptions(internalDownloadDir, externalDownloadDir, sourceUpdateIntervalHours,
        quoteSourceConverters, ValidateCanonicalSchema),
    sp.GetRequiredService<ILogger<SourceCacheUpdater>>()));

// #153: a generated ruleFile/sourceAliasFile override is written under the same two persistent,
// writable cache directories SourceCacheUpdater already uses above — never the bundled/image sources
// directory, which is read-only in a real deployment.
builder.Services.AddSingleton<IRuleFileOverridePathResolver>(_ =>
    new RuleFileOverridePathResolver(internalDownloadDir, externalDownloadDir, bundledSourcesDir, importsDir));

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
        sp.GetRequiredService<IImportActionWriter>(),
        sp.GetRequiredService<IAuditEntryWriter>(),
        sp.GetRequiredService<ICallerContext>(),
        sp.GetRequiredService<ILogger<DatabaseInitializer>>(),
        sp.GetRequiredService<ISourceCacheUpdater>(),
        autoUpdateSources,
        autoPurgeBundledImportActions,
        autoPurgeUserImportActions,
        sp.GetRequiredService<IRuleFileOverridePathResolver>(),
        sp.GetRequiredService<ISourceFileOverrideRegistry>(),
        sp.GetRequiredService<IFileResourceRepository>(),
        QuotinatorMigrations.Baseline,
        sp.GetRequiredService<IDiskSpaceProvider>());
});
// #285: resolves a Conversation's per-line quote/stage-direction/sound-cue lookups via
// JoinQueryRepository/IJoinStrategy per ADR 017.
builder.Services.AddSingleton<IJoinStrategy<QuoteRow>, QuoteLineStrategy>();
builder.Services.AddSingleton<JoinQueryRepository<QuoteRow>>();
builder.Services.AddSingleton<IJoinStrategy<StageDirectionLineRow>, StageDirectionLineStrategy>();
builder.Services.AddSingleton<JoinQueryRepository<StageDirectionLineRow>>();
builder.Services.AddSingleton<IJoinStrategy<SoundCueLineRow>, SoundCueLineStrategy>();
builder.Services.AddSingleton<JoinQueryRepository<SoundCueLineRow>>();
builder.Services.AddSingleton<IQuoteService>(sp => new Quotinator.Core.Services.SqliteQuoteService(
    connectionFactory,
    unicodeAwareSearch,
    sp.GetRequiredService<JoinQueryRepository<QuoteRow>>(),
    sp.GetRequiredService<JoinQueryRepository<StageDirectionLineRow>>(),
    sp.GetRequiredService<JoinQueryRepository<SoundCueLineRow>>()));
builder.Services.AddSingleton<Quotinator.Core.Services.IQuoteImportService>(sp => new Quotinator.Core.Services.SqliteQuoteImportService(
    connectionFactory,
    sp.GetRequiredService<IImportBatchRepository>(),
    sp.GetRequiredService<IImportActionCoordinator>(),
    sp.GetRequiredService<IImportActionService>(),
    sp.GetRequiredService<IImportActionReader>(),
    quoteSourceConverters,
    configPolicy,
    sp.GetRequiredService<IFileResourceRepository>()));
builder.Services.AddSingleton<RequestLoggingMiddleware>();
builder.Services.AddSingleton<Quotinator.Api.Startup.DatabaseHealthState>();
builder.Services.AddSingleton<Quotinator.Api.Startup.StartupUxState>();
builder.Services.AddSingleton<Quotinator.Api.Startup.StartupPhaseState>();
builder.Services.AddSingleton<DatabaseHealthGateMiddleware>();
builder.Services.AddSingleton<StartupWaitMiddleware>();
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
    options.DefaultRequestCulture = new RequestCulture("en-GB");
    options.AddSupportedCultures(SupportedCultures);
    options.AddSupportedUICultures(SupportedCultures);
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

// #280: database initialisation now runs after Kestrel starts listening (see the StartAsync/
// WaitForShutdownAsync split at the bottom of this file) — StartupWaitMiddleware serves a wait page
// for every non-exempt request until it completes, instead of the app being completely unreachable
// during this window as it was before. dbHealth is still resolved here since it's referenced by name
// throughout the rest of this section's setup.
var dbHealth = app.Services.GetRequiredService<Quotinator.Api.Startup.DatabaseHealthState>();

var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
var logger   = app.Services.GetRequiredService<ILogger<Program>>();
lifetime.ApplicationStopping.Register(() =>
    logger.LogServerStopping(versionService.Version));

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

// Degrades to a clear 503 (instead of a raw per-request exception) once DatabaseHealthState
// records a failed startup initialisation — see DatabaseHealthGateMiddleware's own remarks. Must
// run before request logging/exception handling so a degraded request never reaches a handler
// that would throw.
app.UseMiddleware<DatabaseHealthGateMiddleware>();

// Optional request logging — logs every endpoint call as two lines (start + end) with a
// per-request correlation ID. Off by default. Enable with log_requests: true in the add-on
// config (or Quotinator__LogRequests=true). All endpoints are logged; header values are never
// captured (X-Api-Key, Authorization, Cookie must not appear in logs).
//
// Registered before UseExceptionHandler() so it wraps it, not the reverse — the completion log
// line reads context.Response.StatusCode in a finally block, and an exception thrown deeper in
// the pipeline unwinds through that finally before the response status has actually been set by
// whichever middleware handles it. Logging registered after UseExceptionHandler would therefore
// always report the pre-exception default (200), never the real status the client received.
if (logRequests)
    app.UseMiddleware<RequestLoggingMiddleware>();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRequestLocalization();

// #280: gates every non-exempt request to a wait page while StartupPhaseState.IsComplete is false.
// Registered after UseRequestLocalization() so the page's text resolves from Accept-Language, and
// before UseRateLimiter() so a polling wait page never burns the caller's rate-limit budget.
app.UseMiddleware<StartupWaitMiddleware>();

app.UseRateLimiter();

// Populate ICallerContext.Agent from the User-Agent header for audit trail entries.
// Only the value is read — the header name is not logged or stored anywhere.
app.Use(async (context, next) =>
{
    var callerContext = context.RequestServices.GetRequiredService<ICallerContext>();
    callerContext.Agent = context.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;
    await next();
});

app.MapOpenApi();
app.MapScalarApiReference();

app.UseAntiforgery();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet(ApiRoutes.Health, (Quotinator.Api.Startup.DatabaseHealthState dbHealth, Quotinator.Api.Startup.StartupPhaseState startupPhase) =>
    !startupPhase.IsComplete
        ? Results.Json(new { status = "starting" }, statusCode: StatusCodes.Status503ServiceUnavailable)
        : dbHealth.IsHealthy
            ? Results.Ok(new { status = "healthy" })
            : Results.Json(new { status = "unhealthy", reason = dbHealth.FailureReason }, statusCode: StatusCodes.Status503ServiceUnavailable))
   .WithName("Health")
   .WithTags(ApiTags.System)
   .WithSummary("Health check")
   .WithDescription("Returns the current health status of the API. While startup database initialisation is still running, returns a distinct \"starting\" status (503) so callers can tell that apart from a genuine failure; once complete, reports \"healthy\" (200) or \"unhealthy\" (503) depending on whether initialisation succeeded.");

app.MapGet(ApiRoutes.Version, (IVersionService vs, IWebHostEnvironment env, IDatabaseInitializer db, Quotinator.Api.Startup.StartupPhaseState startupPhase) =>
    !startupPhase.IsComplete
        ? Results.Ok(new { status = "starting", version = vs.Version })
        : Results.Ok(new
        {
            status      = "ready",
            version     = vs.Version,
            environment = env.EnvironmentName,
            database    = new
            {
                schemaVersion   = db.SchemaVersion,
                quotes          = db.QuoteCount,
                sources         = db.SourceCount,
                characters      = db.CharacterCount,
                people          = db.PeopleCount,
                series          = db.SeriesCount,
                universes       = db.UniverseCount,
                stageDirections = db.StageDirectionCount,
                soundCues       = db.SoundCueCount,
                conversations   = db.ConversationCount
            }
        }))
   .WithName("Version")
   .WithTags(ApiTags.System)
   .WithSummary("API version")
   .WithDescription("Returns the running version, environment, and database schema version with row counts. While startup database initialisation is still running, returns only {\"status\":\"starting\",\"version\":...} — the environment/database fields don't exist yet.");

app.MapQuoteEndpoints();
app.MapAdminEndpoints();
app.MapImportEndpoints();
app.MapImportRuleEndpoints();
app.MapImportFileResourceEndpoints();
app.MapImportBatchEndpoints();
app.MapNotificationEndpoints();
app.MapConversationEndpoints();
app.MapSourceEndpoints();
app.MapCharacterEndpoints();
app.MapPersonEndpoints();
app.MapSeriesEndpoints();
app.MapUniverseEndpoints();
app.MapStageDirectionEndpoints();
app.MapSoundCueEndpoints();

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

// #280: Kestrel is now listening — StartupWaitMiddleware is already serving a wait page for every
// non-exempt request (registered above, before this point was reached), so initialisation runs here,
// after StartAsync, instead of before it as it did prior to #280.
await app.StartAsync();

// #309: force eager construction of the changelog database's keep-alive connection — DI singletons
// are otherwise resolved lazily, on first use, which would be too late (the shared-cache in-memory
// database wouldn't exist yet when the changelog schema step below needs it). Wrapped separately from
// the main database's own init try/catch below: a changelog-database failure must never affect the
// main database's own initialisation or health status, matching ADR 018's fallback requirement
// (IChangelogReader, once built, falls back to the JSON-file-based IChangelogService regardless of why
// the changelog database is unavailable). Schema creation itself stays synchronous here — it's a single
// connection and a couple of DDL statements, fast enough not to matter — but the content refresh below
// is deliberately NOT awaited inline; see that block's own comment.
try
{
    app.Services.GetRequiredService<ChangelogConnectionKeepAlive>();
    await app.Services.GetRequiredService<ChangelogDatabaseInitializer>().InitialiseAsync();
}
catch (Exception ex)
{
    app.Services.GetRequiredService<ILogger<Program>>()
        .LogWarning(ex, "[Database - Init] failed to initialise the changelog database — " +
            "non-fatal, startup continues. The changelog will fall back to reading its JSON files directly.");
}

// #309: the changelog content refresh (one atomic parent+children insert per release, across every
// loaded language) runs detached in the background rather than being awaited here — found live: awaiting
// it inline pushed StartupPhaseState.MarkComplete() (below) meaningfully later, widening the window a
// request can observe "starting" instead of the app's real health, for content whose own read path
// (IChangelogReader, once built) already tolerates the changelog database not being ready yet by falling
// back to the JSON-backed IChangelogService — the same fallback it uses for a genuine failure. There is
// nothing else in this process that can race the keyed changelog connection factory before this runs.
_ = Task.Run(async () =>
{
    try
    {
        await app.Services.GetRequiredService<ChangelogSystemContentImporter>().RefreshAsync();
    }
    catch (Exception ex)
    {
        app.Services.GetRequiredService<ILogger<Program>>()
            .LogWarning(ex, "[Database - Import] failed to refresh changelog content — non-fatal, " +
                "startup continues. The changelog will fall back to reading its JSON files directly.");
    }
});

// #81: capture the version this app instance was running as of its *previous* healthy startup,
// before migrations run — a missing System_AppVersion table (a fresh install, or the very first boot
// after this table was introduced) reads as null, meaning "nothing to catch up on, only the current
// version matters" (WhatsNewNotification.BuildSeeds' own fresh-install rule). Read here, synchronously
// and fast (a single row against the main database, matching #279's/#289's own producers' own
// synchronous read+write — unlike #309's changelog database, there is no separate, slower connection
// factory involved), and used further down once the current version is known to be running healthily.
var appVersionTracker = app.Services.GetRequiredService<IAppVersionTracker>();
var lastActiveVersion = await appVersionTracker.GetLastActiveVersionAsync();

// A database initialisation failure must never crash the whole process outright — that would
// also make POST /api/v1/admin/database/reset unreachable, the one endpoint actually capable of
// resolving the underlying schema/version mismatch (found live, 2026-08-02: exiting on this
// exception meant the operator's own documented remedy could never be reached). Catching it here
// logs one clear, actionable message, then records the failure on DatabaseHealthState and lets
// startup continue: the app still binds and stays reachable for health/version/admin traffic, while
// DatabaseHealthGateMiddleware degrades every other request to a clear 503 instead of letting it
// throw the same raw exception per-request.
try
{
    await dbInitializer.InitialiseAsync();
}
catch (DatabaseBackupWriteException ex)
{
    var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
    const string failureReason =
        "Database initialisation failed while writing a pre-change safety backup. This usually " +
        "means the data directory ran out of disk space or lost write access mid-write. Resolve " +
        "by freeing disk space or restoring write access, then restart.";
    startupLogger.LogStartupDatabaseInitFailed(ex, failureReason);
    dbHealth.MarkFailed(failureReason);
}
catch (Exception ex)
{
    var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
    const string failureReason =
        "Database initialisation failed. This often means the database's recorded schema " +
        "version doesn't match its actual on-disk schema (e.g. after an interrupted upgrade). " +
        "Resolve with an explicit database Reset (POST /api/v1/admin/database/reset) or by " +
        "stopping the app, deleting the database file, and restarting.";
    startupLogger.LogStartupDatabaseInitFailed(ex, failureReason);
    dbHealth.MarkFailed(failureReason);
}

// #279: first concrete producer for #278's notification mechanism — announces the two breaking
// operationId renames this release ships. Idempotent across restarts (checked via NotificationSeeding's
// own dedupe-key lookup against notification history), so this call is safe to leave in place
// indefinitely rather than needing to be removed after the first deploy. Deliberately outside the
// critical DB-init try/catch above and in its own non-fatal guard: a failure here (e.g. a test's
// NoOpDatabaseInitializer, which never creates System_Notification) must never mark the whole app
// unhealthy — writing an announcement notification is inherently non-critical, unlike schema init itself.
if (dbHealth.IsHealthy)
{
    try
    {
        await Quotinator.Api.Startup.NotificationSeeding.SeedOnceAsync(
            app.Services.GetRequiredService<INotificationReader>(),
            app.Services.GetRequiredService<INotificationWriter>(),
            NotificationType.Warning,
            dedupeKey: "GetAllImportBatches",
            message: "Two REST API operation IDs were renamed for naming consistency (issue #279): " +
                      "GetImportBatches → GetAllImportBatches, and GetFileResources → GetAllFileResources. " +
                      "This only affects a generated API client keyed by operation ID — routes and behaviour are unchanged.");
    }
    catch (Exception ex)
    {
        app.Services.GetRequiredService<ILogger<Program>>()
            .LogWarning(ex, "[Server] Failed to seed the #279 operation-id-rename notification — non-fatal, startup " +
                "continues. This does not mean the database is broken or corrupted: it means a table the current " +
                "schema version implies should exist (e.g. System_Notification) is actually missing on disk, a " +
                "mismatch normal operation shouldn't produce.");
    }
}

// #289: second producer for #278's notification mechanism — announces a schema-version-overshoot
// (the recorded version exceeds this build's own known migration count, which only happens after a
// migration squash on a database that already applied the pre-squash migrations). The dedupe key
// includes the actual detected versions, not a fixed string like #279's: repeats of the same
// already-notified overshoot state (e.g. the operator hasn't reset yet and the app just restarted)
// stay deduped, but a genuinely different future overshoot (a later squash producing different
// version numbers) still gets its own notification. ActionRequired + DatabaseReset dismiss trigger:
// POST /admin/database/reset already calls DismissByTriggerAsync(NotificationDismissTrigger.DatabaseReset)
// (see AdminEndpoints.cs), so this clears itself automatically once the operator resets.
if (dbHealth.IsHealthy && dbInitializer.SchemaVersionOvershootDetected)
{
    try
    {
        var overshootDedupeKey = $"SchemaVersionOvershoot:data-v{dbInitializer.DataSchemaVersion}-app-v{dbInitializer.SchemaVersion}";
        await Quotinator.Api.Startup.NotificationSeeding.SeedOnceAsync(
            app.Services.GetRequiredService<INotificationReader>(),
            app.Services.GetRequiredService<INotificationWriter>(),
            NotificationType.ActionRequired,
            dedupeKey: overshootDedupeKey,
            message: $"This database's recorded schema version (data v{dbInitializer.DataSchemaVersion}, " +
                      $"app v{dbInitializer.SchemaVersion}) is ahead of what this build expects — usually because " +
                      "a set of not-yet-released migrations were consolidated after this database already applied " +
                      "them individually (issue #289). The schema itself is complete and the app is working " +
                      "normally; running a database Reset (POST /api/v1/admin/database/reset) will true up the " +
                      "version bookkeeping.",
            trigger: NotificationDismissTrigger.DatabaseReset);
    }
    catch (Exception ex)
    {
        app.Services.GetRequiredService<ILogger<Program>>()
            .LogWarning(ex, "[Server] Failed to seed the #289 schema-version-overshoot notification — non-fatal, startup continues.");
    }
}

// #81: System_AppVersion is meant to always carry the current version once startup is healthy —
// the same "structurally required, not the caller's optional content" reasoning CLAUDE.md's endpoint
// side-effect policy applies elsewhere, just applied to a startup step instead of an endpoint. Fast,
// synchronous, single-row write against the already-open main database (matching #279's/#289's own
// synchronous read+write producers) — safe to await inline, unlike #309's changelog database or the
// slower catch-up logic below.
if (dbHealth.IsHealthy)
{
    try
    {
        await appVersionTracker.RecordCurrentVersionAsync(versionService.Version);
    }
    catch (Exception ex)
    {
        app.Services.GetRequiredService<ILogger<Program>>()
            .LogWarning(ex, "[Server] Failed to record the current app version — non-fatal, startup continues. " +
                "The what's-new notification's catch-up range may be inaccurate on the next restart.");
    }
}

// #81: third producer for #278's notification mechanism — announces every release's
// notification-flagged changelog highlights (#307's ChangelogReservedAudience.Notification
// convention) missed since lastActiveVersion (captured above, before migrations ran), one
// notification per release. Reads via IChangelogReader (#309), which falls back to the JSON-backed
// IChangelogService on its own if the changelog database isn't ready or available — this producer
// doesn't need to know or care which path served the document. "Seen" state is the existing
// notification history itself (dismissing via POST /notifications/{id}/dismiss stops it reappearing);
// no separate cookie or localStorage marker is needed. Runs detached, like #309's own changelog-import
// task — found live: awaiting IChangelogReader.GetDocumentAsync inline here delayed
// StartupPhaseState.MarkComplete() enough to reintroduce the exact race #309's Step 6 fix already
// solved once, this time affecting far more of the test suite since every WebApplicationFactory-based
// test spins up its own full startup sequence. Timing relative to MarkComplete() doesn't matter for
// correctness here, the same as #279's/#289's producers — writing an announcement is inherently
// non-critical.
if (dbHealth.IsHealthy)
{
    _ = Task.Run(async () =>
    {
        try
        {
            var whatsNewDocument = await app.Services.GetRequiredService<IChangelogReader>().GetDocumentAsync(null);
            await Quotinator.Api.Startup.WhatsNewNotification.SeedAsync(
                app.Services.GetRequiredService<INotificationReader>(),
                app.Services.GetRequiredService<INotificationWriter>(),
                whatsNewDocument,
                lastActiveVersion,
                versionService.Version);
        }
        catch (Exception ex)
        {
            app.Services.GetRequiredService<ILogger<Program>>()
                .LogWarning(ex, "[Server] Failed to seed the #81 what's-new notification — non-fatal, startup continues.");
        }
    });
}

// #280: initialisation (successful or not) is now finished — StartupWaitMiddleware stops
// intercepting requests from this point on. Marked complete regardless of dbHealth's outcome: a
// failed startup has its own existing degraded-state UI (DatabaseHealthGateMiddleware/#263's
// modals), not the wait page.
app.Services.GetRequiredService<Quotinator.Api.Startup.StartupPhaseState>().MarkComplete();

// "Ready" now means truly ready (initialisation complete), not merely "Kestrel bound" — logged
// directly here instead of via the ApplicationStarted event hook, which fires as soon as StartAsync
// returns, before initialisation even begins under this model.
var readyAddresses = (app.Services
    .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
    .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()
    ?.Addresses ?? []).ToList();
startupLog.LogReady(readyAddresses);

await app.WaitForShutdownAsync();

// Exposes Program to WebApplicationFactory<Program> in the test project.
public partial class Program
{
    private static readonly string[] SupportedCultures = ["en-GB", "de", "nl"];
}

using System.Globalization;
using System.Text.Json.Nodes;
using Serilog;
using Serilog.Events;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.Sqlite;
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
using Quotinator.Data.Entities;
using Quotinator.Core.Database;
using Quotinator.Core.Entities;
using Quotinator.Core.Helpers;
using Quotinator.Core.Queries;
using Quotinator.Core.Repositories;
using Quotinator.Core.Services;
using Quotinator.Api.Middleware;
using Quotinator.Api.OpenApi;
using Quotinator.Api.Services;
using Quotinator.Data.Http;
using Quotinator.Data.Import;
using Quotinator.Data.Notifications;
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
using System.Text.Json;
using Quotinator.Api.Startup;
using Quotinator.Changelog.Models;

new QuotinatorDapperConfiguration().Configure();

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Read HA add-on options from /data/options.json when running inside the supervisor.
// The supervisor writes the user's config panel values here; env_vars template rendering
// is not reliably supported for optional options. This is the official HA approach.
string haOptionsPath = "/data/options.json";
bool isHa = File.Exists(haOptionsPath);
if (isHa)
{
    JsonElement haOptions = System.Text.Json.JsonDocument.Parse(File.ReadAllText(haOptionsPath)).RootElement;
    Dictionary<string, string?> haMap = [];
    if (haOptions.TryGetProperty("log_level",     out JsonElement ll))  haMap["Quotinator:LogLevel"]    = ll.GetString();
    if (haOptions.TryGetProperty("log_requests",  out JsonElement lr))  haMap["Quotinator:LogRequests"] = lr.GetRawText();
    if (haOptions.TryGetProperty("ssl",           out JsonElement ssl)) haMap["Quotinator:Ssl"]         = ssl.GetRawText();
    if (haOptions.TryGetProperty("certfile",      out JsonElement cf))  haMap["Quotinator:SslCertFile"] = $"/ssl/{cf.GetString()}";
    if (haOptions.TryGetProperty("keyfile",       out JsonElement kf))  haMap["Quotinator:SslKeyFile"]  = $"/ssl/{kf.GetString()}";
    if (haOptions.TryGetProperty("admin_api_key", out JsonElement ak))  haMap["Quotinator:AdminApiKey"] = ak.GetString();
    if (haOptions.TryGetProperty("backup_path",   out JsonElement bp))  haMap["Quotinator:BackupPath"]  = bp.GetString();
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
            new() { Name = ApiTags.Notifications, Description = "Endpoints for listing startup and maintenance notifications, and for dismissing them. Dismissing requires `X-Api-Key` authentication; listing does not." },
            new() { Name = ApiTags.Backup,        Description = "Endpoints for managing database backups — listing what exists, taking one on demand, downloading one so it survives the container, removing one to free quota, and reporting whether a backup can be taken right now. All require `X-Api-Key` authentication and share the Admin endpoints' concurrency-1 limiter. They remain reachable while the database is degraded, which is the state they exist for." },
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
        IApiLocalizer localizer = context.HttpContext.RequestServices.GetRequiredService<IApiLocalizer>();
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
string dataDir = builder.Configuration["Quotinator:DataDir"]
    ?? HaFallbackDir()
    ?? Path.Combine(AppContext.BaseDirectory, "data");

// #326: one cause, one message, wherever it is first noticed — the database failing to open, or a
// directory failing to be created below. DatabaseHealthState.MarkFailed is first-wins, so whichever
// gets there first, the operator sees the same actionable text rather than two descriptions of one
// problem. Deliberately says a Reset cannot help: the generic database-init reason recommends exactly
// that, and following it here wastes the operator's time on an operation that also writes.
const string DataDirectoryNotWritableReason =
    "The data directory cannot be written. This usually means the volume is mounted read-only, or " +
    "the container user lacks write permission on it. Restore write access to the data directory " +
    "and restart. A database Reset cannot resolve this — it writes too.";

// Walks the chain because the failure can arrive nested: DatabaseInitializer restores its backup and
// rethrows on any migration exception, so the SqliteException that actually describes the cause is not
// always the outermost one.
static bool IsDataDirectoryNotWritable(Exception? exception)
{
    for (Exception? current = exception; current is not null; current = current.InnerException)
    {
        // 14 SQLITE_CANTOPEN — the directory cannot be written, so SQLite cannot create the file or
        // its -shm wal-index. 8 SQLITE_READONLY — the directory is writable but the file is not.
        if (current is SqliteException sqlite && sqlite.SqliteErrorCode is 14 or 8) return true;
        if (current is UnauthorizedAccessException or IOException) return true;
    }

    return false;
}

// #326: this and the keys/ creation below both run before app.StartAsync(), so an unguarded throw
// here kills the process before Kestrel binds — no wait page, no /health, no OpenAPI, nothing to tell
// the operator what happened. Recorded and reported once dbHealth exists rather than thrown.
string? dataDirectoryFailure = null;
try
{
    Directory.CreateDirectory(dataDir);
}
catch (Exception ex) when (IsDataDirectoryNotWritable(ex))
{
    dataDirectoryFailure = DataDirectoryNotWritableReason;
}

// Duplicate-resolution policy from config — lowest-priority tier; a manifest's own
// duplicateResolution section overrides this when present. Quotinator:DefaultConflictPolicy is a
// flat key (env Quotinator__DefaultConflictPolicy) — the 5 nested per-type keys below keep their
// existing paths, minus the now-redundant "Default" sibling that used to live under
// Quotinator:DuplicateResolution. Parsing itself lives in ConflictPolicyParser (Quotinator.Data)
// so it's unit-testable outside these top-level statements.
ManifestPolicy configPolicy = new(
    Default:      ConflictPolicyParser.Parse(builder.Configuration["Quotinator:DefaultConflictPolicy"]),
    Quotes:       ConflictPolicyParser.ParseNullable(builder.Configuration["Quotinator:DuplicateResolution:Quotes"]),
    Sources:      ConflictPolicyParser.ParseNullable(builder.Configuration["Quotinator:DuplicateResolution:Sources"]),
    Characters:   ConflictPolicyParser.ParseNullable(builder.Configuration["Quotinator:DuplicateResolution:Characters"]),
    People:       ConflictPolicyParser.ParseNullable(builder.Configuration["Quotinator:DuplicateResolution:People"]),
    Translations: ConflictPolicyParser.ParseNullable(builder.Configuration["Quotinator:DuplicateResolution:Translations"]));

bool createMissingManifest  = builder.Configuration.GetValue("Quotinator:CreateMissingManifest", true);
bool includeDefaultSources  = builder.Configuration.GetValue("Quotinator:IncludeDefaultSources", true);

// Auto-update: whether the app checks manifest downloadUrl/github entries for a fresher copy at
// all (master switch — false means pure offline mode, no network calls ever), and how long a
// downloaded copy is considered fresh before the next check re-verifies it.
bool autoUpdateSources        = builder.Configuration.GetValue("Quotinator:AutoUpdateSources", true);
int sourceUpdateIntervalHours = builder.Configuration.GetValue("Quotinator:SourceUpdateIntervalHours", 24);
int sourceRefreshTimeoutSeconds = builder.Configuration.GetValue<int?>("Quotinator:SourceRefreshTimeoutSeconds")
    ?? SourceCacheUpdater.DefaultHttpTimeoutSeconds;
int sourceRefreshConnectTimeoutSeconds = builder.Configuration.GetValue<int?>("Quotinator:SourceRefreshConnectTimeoutSeconds")
    ?? SourceCacheUpdater.DefaultConnectTimeoutSeconds;

// #249: once a seeded batch reaches zero pending actions, its Import_Action (conflict-resolution)
// rows have served their purpose and are purged automatically — separate settings per origin so a
// developer investigating one specific source (bundled or user-imports) can temporarily retain that
// origin's resolution history without affecting the other.
bool autoPurgeBundledImportActions = builder.Configuration.GetValue("Quotinator:AutoPurgeBundledImportActions", true);
bool autoPurgeUserImportActions    = builder.Configuration.GetValue("Quotinator:AutoPurgeUserImportActions", true);

// Unicode-aware LIKE-style matching (issue #222) — opt-in, off by default until validated against
// real-world non-ASCII search traffic. See docs/milestones/maintenance-milestone-v1.8.0/
// 222-unicode-like-matching-plan.md for why this isn't unconditional.
bool unicodeAwareSearch = builder.Configuration.GetValue("Quotinator:UnicodeAwareSearch", false);

// Bundled sources are always read from the Docker image (AppContext.BaseDirectory/data/sources/).
// No file copy to the persistent volume is needed — only the database and DataProtection keys
// need to be on a writable, persistent path.
string bundledSourcesDir = Path.Combine(AppContext.BaseDirectory, "data", DataPaths.SourcesFolder);

// User imports: optional directory in the data volume. Create it so users can drop files in.
// Quotinator:ImportsPath overrides the default location when set.
string importsDir = builder.Configuration["Quotinator:ImportsPath"] is { Length: > 0 } customImportsPath
    ? customImportsPath
    : Path.Combine(dataDir, DataPaths.ImportsFolder);

// Auto-update download caches — always under the persistent data volume, never the read-only
// bundled image path, so both are writable in every deployment shape including the HA add-on.
// "Internal" is the default cache for bundled-manifest entries; "external" for user-imports entries.
string internalDownloadDir = Path.Combine(dataDir, DataPaths.SourcesFolder, DataPaths.DownloadedSourcesFolder);
string externalDownloadDir = Path.Combine(dataDir, DataPaths.ImportsFolder, DataPaths.DownloadedSourcesFolder);

// Persist DataProtection keys to a subdirectory of the data volume so antiforgery tokens
// and Blazor circuit descriptors survive container restarts and add-on updates.
string keysDir = Path.Combine(dataDir, DataPaths.DataProtectionFolder);

// #326: same guard, same reason as the data directory above. PersistKeysToFileSystem stays registered
// regardless: no ephemeral-key fallback is introduced here (CLAUDE.md's DataProtection rule), so a
// DataProtection failure surfaces per-request while degraded instead of at startup. Adding a fallback
// chain for the keys location is #332's, not this issue's.
try
{
    Directory.CreateDirectory(keysDir);
}
catch (Exception ex) when (IsDataDirectoryNotWritable(ex))
{
    dataDirectoryFailure ??= DataDirectoryNotWritableReason;
}
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
bool   sslEnabled  = builder.Configuration.GetValue<bool>("Quotinator:Ssl");
string sslCertFile = builder.Configuration["Quotinator:SslCertFile"] ?? string.Empty;
string sslKeyFile  = builder.Configuration["Quotinator:SslKeyFile"]  ?? string.Empty;
bool   isContainer = string.Equals(
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
string bundledChangelogDir = Path.Combine(AppContext.BaseDirectory, "data", DataPaths.ChangelogFolder);
builder.Services.AddSingleton<IChangelogService>(sp =>
    new ChangelogService(
        bundledChangelogDir,
        sp.GetRequiredService<ILogger<ChangelogService>>()));

string dbPath     = Path.Combine(dataDir, DataPaths.DatabaseFile);
string backupsDir = builder.Configuration["Quotinator:BackupPath"] is { Length: > 0 } customBackupPath
    ? customBackupPath
    : Path.Combine(dataDir, DataPaths.BackupsFolder);
int             maxBackupStorageGb = builder.Configuration.GetValue("Quotinator:MaxBackupStorageGb", 1);
int             backupQuotaPercent = builder.Configuration.GetValue("Quotinator:BackupQuotaPercent", DatabaseOptions.DefaultBackupQuotaPercent);
DatabaseOptions dbOptions          = new() { DbPath = dbPath, BackupsPath = backupsDir, MaxBackupStorageGb = maxBackupStorageGb, BackupQuotaPercent = backupQuotaPercent };
// useMemoryTempStore: true — see SqliteConnectionFactory.cs's own comment for the #294 incident this
// opts into working around. Safe here because Quotinator's own dataset (hundreds to low-thousands of
// quotes) makes the resulting RAM cost negligible; Quotinator.Data itself stays unopinionated and
// defaults to false since it doesn't know a future consumer's dataset size.
SqliteConnectionFactory connectionFactory = new(dbPath, useMemoryTempStore: true);
builder.Services.AddSingleton<IDiskSpaceProvider, DiskSpaceProvider>();

// #349: registered so the backup endpoints' reader and writer resolve the same folder and the same
// storage budget the initializer was built with, rather than each rebuilding an opinion about where
// backups live. The reader and writer never open the database, which is what keeps those endpoints
// answerable while it is degraded.
builder.Services.AddSingleton(dbOptions);
builder.Services.AddSingleton<IDatabaseBackupReader>(sp =>
    new DatabaseBackupReader(dbOptions, sp.GetRequiredService<IDiskSpaceProvider>()));
builder.Services.AddSingleton<IDatabaseBackupWriter>(_ => new DatabaseBackupWriter(dbOptions));
builder.Services.AddSingleton<IDbConnectionFactory>(_ => connectionFactory);

// #309: separate database for changelog content (ADR 018) — no relational or transactional coupling
// to domain data, so it lives outside quotinatordata.db entirely, as its own file beside it.
//
// Step 14: this was a shared-cache in-memory database held open by a dedicated keep-alive connection.
// That storage mode is destroyed the moment its last connection closes, and was found live to take the
// database-backed read path down thirteen minutes into a run — silently, because the JSON fallback
// covered for it. A file has no such lifetime. Its contents are rebuilt from the bundled JSON at every
// startup, so nothing user-authored is ever stored here and neither Reset nor the pre-migration backup
// touches it. useMemoryTempStore: true for the same #294 reason as the main database.
string changelogDbPath = Path.Combine(dataDir, DataPaths.ChangelogDatabaseFile);
SqliteConnectionFactory changelogConnectionFactory = new(changelogDbPath, useMemoryTempStore: true);
builder.Services.AddKeyedSingleton<IDbConnectionFactory>(
    DatabaseConnectionKeys.Changelog, (_, _) => changelogConnectionFactory);
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
// Singleton: the import concludes once per process, and every reader must observe that same outcome.
builder.Services.AddSingleton<IChangelogImportReadiness, ChangelogImportReadiness>();
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
// #319: JoinQueryRepository/IJoinStrategy per ADR 017 — the notification reads became two-table
// projections over System_NotificationTranslation. Registered through the service-provider factory
// overload rather than AddSingleton<JoinQueryRepository<T>>() like the joins above, because all three
// notification strategies return the same NotificationEntity: three registrations of one closed
// generic would collapse to whichever landed last. The alternative — three identical row types whose
// only purpose is to make DI's type-based resolution work — would read worse than the problem it
// solves. Per CLAUDE.md's DI policy this is the factory overload's intended use, not a bare `new`.
builder.Services.AddSingleton<INotificationReader>(sp => new NotificationReader(
    sp.GetRequiredService<IDbConnectionFactory>(),
    new JoinQueryRepository<NotificationEntity>(
        sp.GetRequiredService<IDbConnectionFactory>(), new NotificationJoinStrategies.Active()),
    new JoinQueryRepository<NotificationEntity>(
        sp.GetRequiredService<IDbConnectionFactory>(), new NotificationJoinStrategies.Page())));
builder.Services.AddSingleton<INotificationWriter, NotificationWriter>();
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
// #323: the primary handler must be configured explicitly. SocketsHttpHandler's ConnectTimeout and
// PooledConnectionLifetime both default to infinite, so a stalled connect has no budget of its own and
// a pooled connection never rotates — see SourceCacheUpdater's two Default* constants for the full why.
builder.Services
    .AddHttpClient(SourceCacheUpdater.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(sourceRefreshTimeoutSeconds))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        ConnectTimeout           = TimeSpan.FromSeconds(sourceRefreshConnectTimeoutSeconds),
        PooledConnectionLifetime = TimeSpan.FromMinutes(SourceCacheUpdater.DefaultPooledConnectionLifetimeMinutes),

        // No ConnectCallback (#325, reverted). A manifest entry is a plain download link — an ordinary
        // URI or an IP-based one — and it is resolved and fetched as such, by the default handler. The
        // custom address-family race that briefly lived here was disproportionate to what it protected:
        // a source refresh is best-effort, SourceCacheUpdater already falls back to the local copy when
        // a download fails, and the refresh runs again next cycle. What it cost was a family preference
        // that overrode the operating system's own policy, a dependency on undocumented resolver
        // ordering, and connect-cancellation noise that reads as a fault in a debugger.
        //
        // ConnectTimeout above is the part that earns its place: it bounds the failure instead of
        // letting a routed-but-unreachable path hang. A first attempt that fails is a retry's problem
        // (#329), not a reason to take over connection establishment.
    });

// Converters are stateless, hardcoded per source — no DI registration needed for the individual
// plugin instances themselves (CLAUDE.md's DI policy: bare `new` is permitted for a computed value
// assembled before a factory closure, same shape already used for SourceCacheOptions itself).
Dictionary<string, IQuoteSourceConverter> quoteSourceConverters = new IQuoteSourceConverter[]
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
    ILogger<Program> logger = sp.GetRequiredService<ILogger<Program>>();
    LegacyConfigWarnings.WarnIfDataPathStillSet(builder.Configuration["Quotinator:DataPath"], logger);

    IReadOnlyList<SeedBatch> seedBatches = SeedBatchesBuilder.Build(
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
string haLogLevel = builder.Configuration["Quotinator:LogLevel"] ?? "info";
LogEventLevel serilogLevel = haLogLevel.ToLowerInvariant() switch
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
    bool isDev = ctx.HostingEnvironment.IsDevelopment();
    string template = isDev
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

WebApplication app = builder.Build();

IDatabaseInitializer dbInitializer  = app.Services.GetRequiredService<IDatabaseInitializer>();
IVersionService versionService = app.Services.GetRequiredService<IVersionService>();
bool logRequests    = app.Configuration.GetValue<bool>("Quotinator:LogRequests");
bool adminKeyConfigured = !string.IsNullOrEmpty(app.Configuration["Quotinator:AdminApiKey"]);

// StartupSummaryLogger is a one-shot startup utility, not a general-purpose service;
// instantiated directly rather than registered with DI.
Quotinator.Api.Startup.StartupSummaryLogger startupLog = new(
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
DatabaseHealthState dbHealth = app.Services.GetRequiredService<Quotinator.Api.Startup.DatabaseHealthState>();

// #326: a directory that could not be created back at configuration time, reported at the first moment
// there is somewhere to report it to. This is before app.StartAsync(), so the degraded state is in
// place from the very first request rather than racing it.
if (dataDirectoryFailure is not null)
{
    app.Services.GetRequiredService<ILogger<Program>>()
        .LogWarning("[Config] {Reason:l}", dataDirectoryFailure);
    dbHealth.MarkFailed(dataDirectoryFailure);
}

IHostApplicationLifetime lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
ILogger<Program> logger   = app.Services.GetRequiredService<ILogger<Program>>();
lifetime.ApplicationStopping.Register(() =>
    logger.LogServerStopping(versionService.Version));

// Must be first so all subsequent middleware sees the correct scheme and client IP.
app.UseForwardedHeaders();

// The HA supervisor sets X-Ingress-Path to the ingress prefix (e.g. /api/hassio_ingress/TOKEN).
// Applying it as PathBase makes <base href> render correctly so all relative asset URLs
// (blazor.web.js, CSS, etc.) resolve through the ingress proxy rather than HA's own server.
app.Use(async (context, next) =>
{
    if (context.Request.Headers.TryGetValue("X-Ingress-Path", out Microsoft.Extensions.Primitives.StringValues ingressPath)
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
    ICallerContext callerContext = context.RequestServices.GetRequiredService<ICallerContext>();
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
app.MapBackupEndpoints();
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

// #309: initialise the changelog database's schema. Wrapped separately from the main database's own
// init try/catch below: a changelog-database failure must never affect the main database's own
// initialisation or health status, matching ADR 018's fallback requirement (IChangelogReader, once
// built, falls back to the JSON-file-based IChangelogService regardless of why the changelog database
// is unavailable). Schema creation itself stays synchronous here — it's a single connection and a
// couple of DDL statements, fast enough not to matter — but the content refresh below is deliberately
// NOT awaited inline; see that block's own comment.
//
// Step 14 removed a keep-alive connection that was eagerly resolved here: it existed only to stop the
// former shared-cache in-memory database from being destroyed when its last connection closed. A file
// needs no such scaffolding.
try
{
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
    // Every exit path must report an outcome. A reader that finds the database empty waits on this
    // rather than assuming the emptiness is meaningful, so a silent return here would leave it waiting
    // out its whole budget before falling back.
    IChangelogImportReadiness readiness = app.Services.GetRequiredService<IChangelogImportReadiness>();
    try
    {
        await app.Services.GetRequiredService<ChangelogSystemContentImporter>().RefreshAsync();
        readiness.MarkSucceeded();
    }
    catch (Exception ex)
    {
        readiness.MarkFailed();
        app.Services.GetRequiredService<ILogger<Program>>()
            .LogWarning(ex, "[Database - Import] failed to refresh changelog content — non-fatal, " +
                "startup continues. The changelog will fall back to reading its JSON files directly.");
    }
});

IAppVersionTracker appVersionTracker = app.Services.GetRequiredService<IAppVersionTracker>();

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
    ILogger<Program> startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
    const string failureReason =
        "Database initialisation failed while writing a pre-change safety backup. This usually " +
        "means the data directory ran out of disk space or lost write access mid-write. Resolve " +
        "by freeing disk space or restoring write access, then restart.";
    startupLogger.LogStartupDatabaseInitFailed(ex, failureReason);
    dbHealth.MarkFailed(failureReason);
}
// #326: an unwritable data directory is a different fault with a different remedy, and the generic
// reason below actively misdirects for it — it tells the operator to run a database Reset, which also
// writes and therefore cannot work here. Measured by scripts/testing/sqlite-storage-probe.csx: an unwritable
// directory surfaces SQLITE_CANTOPEN (14), a writable directory holding a read-only file surfaces
// SQLITE_READONLY (8). Matched on SqliteErrorCode, never SqliteExtendedErrorCode — the extended code
// varies by cause (526 CANTOPEN_ISDIR for a directory at the database path) while the primary code
// does not.
catch (Exception ex) when (IsDataDirectoryNotWritable(ex))
{
    ILogger<Program> startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
    startupLogger.LogStartupDatabaseInitFailed(ex, DataDirectoryNotWritableReason);
    dbHealth.MarkFailed(DataDirectoryNotWritableReason);
}
catch (Exception ex)
{
    ILogger<Program> startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
    const string failureReason =
        "Database initialisation failed. This often means the database's recorded schema " +
        "version doesn't match its actual on-disk schema (e.g. after an interrupted upgrade). " +
        "Resolve with an explicit database Reset (POST /api/v1/admin/database/reset) or by " +
        "stopping the app, deleting the database file, and restarting.";
    startupLogger.LogStartupDatabaseInitFailed(ex, failureReason);
    dbHealth.MarkFailed(failureReason);
}

// #81: the version this app instance was running as of its *previous* healthy startup — the lower
// bound of the what's-new catch-up range. Read here rather than before migrations, and the ordering is
// load-bearing, not incidental.
//
// It originally ran before InitialiseAsync, on the reasoning that a missing System_AppVersion table
// (a fresh install, or the first boot after #81 introduced it) reads as null — "nothing to catch up
// on". #312 broke that: this query now selects Application and orders by SequenceNumber, columns
// migrations 6 and 7 add, so a database where the table already exists but those columns do not — any
// database at data v4 or v5, i.e. one that ran a build between #81 and #312 — threw
// `no such column: Application` straight past the missing-table catch and killed startup. Found live
// in T1 on exactly such a database; T2 could not have caught it, since it upgraded from v1.8.3, where
// the table does not exist at all and the catch does apply.
//
// Reading after migrations is not a workaround, it is the correct order: migrations 6 and 7 only add
// columns and backfill SequenceNumber — they never touch a recorded Version — so "which version ran
// last" is identical either side of them, while only the later position is guaranteed to have a schema
// matching the query. Widening the catch to swallow `no such column` was rejected: it would leave the
// same trap armed for the next column added to this query, and CLAUDE.md's "no exception-based
// recovery" rule is precisely about not inferring schema state from thrown exceptions.
//
// Still strictly before RecordCurrentAsync below, which is what would overwrite the answer.
//
// #326: gated and guarded, matching RecordCurrentAsync immediately below. This was the one statement
// in the whole post-StartAsync sequence that could still terminate the process: AppVersionTracker
// catches only "no such table: System_AppVersion", so a data directory that cannot be written threw
// SQLITE_CANTOPEN straight past it and killed startup before StartupPhaseState.MarkComplete() —
// taking the degraded UI, /health, the OpenAPI surface and POST /admin/database/reset down with it.
// A failure here leaves lastActiveVersion null, which the #81 producer below already treats as
// "nothing to catch up on", and that producer is itself gated on dbHealth.IsHealthy anyway.
AppVersionRecord? lastActive = null;
if (dbHealth.IsHealthy)
{
    try
    {
        lastActive = await appVersionTracker.GetLastActiveAsync();
    }
    catch (Exception ex)
    {
        app.Services.GetRequiredService<ILogger<Program>>()
            .LogWarning(ex, "[Server] Failed to read the last active app version — non-fatal, startup continues. " +
                "The what's-new notification has no catch-up range this startup.");
    }
}

string? lastActiveVersion = lastActive?.Version;

// #81: System_AppVersion is meant to always carry the current version once startup is healthy —
// the same "structurally required, not the caller's optional content" reasoning CLAUDE.md's endpoint
// side-effect policy applies elsewhere, just applied to a startup step instead of an endpoint. Fast,
// synchronous, single-row write against the already-open main database (matching #279's/#289's own
// synchronous read+write producers) — safe to await inline, unlike #309's changelog database or the
// slower catch-up logic below.
//
// #312 moved this ahead of the notification producers below, which it used to follow. Every producer
// now stamps AppVersionId on what it writes, and a foreign key cannot reference a row that does not
// exist yet. Ordering is safe because lastActiveVersion was captured further up, before migrations
// ran — recording the current version here cannot disturb what the catch-up range already read.
AppVersionRecord? currentVersion = null;
if (dbHealth.IsHealthy)
{
    try
    {
        currentVersion = await appVersionTracker.RecordCurrentAsync(versionService.Application, versionService.Version);
    }
    catch (Exception ex)
    {
        app.Services.GetRequiredService<ILogger<Program>>()
            .LogWarning(ex, "[Server] Failed to record the current app version — non-fatal, startup continues. " +
                "The what's-new notification's catch-up range may be inaccurate on the next restart, and " +
                "notifications written during this startup carry no app-version provenance.");
    }
}

// #279: first concrete producer for #278's notification mechanism — announces the two breaking
// operationId renames this release ships. Idempotent across restarts (NotificationSeeding compares
// this payload structurally against notification history), so this call is safe to leave in place
// indefinitely rather than needing to be removed after the first deploy. Deliberately outside the
// critical DB-init try/catch above and in its own non-fatal guard: a failure here (e.g. a test's
// NoOpDatabaseInitializer, which never creates System_Notification) must never mark the whole app
// unhealthy — writing an announcement notification is inherently non-critical, unlike schema init itself.
if (dbHealth.IsHealthy)
{
    try
    {
        // Hoisted so the payload's content hash covers exactly the text that gets written. Hashing a
        // second copy of the same words would be a copy that can drift.
        const string announcementBody =
            "Two REST API operation IDs were renamed for naming consistency (issue #279): " +
            "GetImportBatches → GetAllImportBatches, and GetFileResources → GetAllFileResources. " +
            "This only affects a generated API client keyed by operation ID — routes and behaviour are unchanged.";

        await NotificationSeeding.SeedOnceAsync(
            app.Services.GetRequiredService<INotificationReader>(),
            app.Services.GetRequiredService<INotificationWriter>(),
            NotificationType.Warning,
            new AnnouncementMetadataDto
            {
                Announcement = "GetAllImportBatches",
                // The release this announcement is about — v1.8.3 shipped the renames — not the version
                // running now, which the row's own AppVersionId records. The two coincide only until
                // the next release.
                ReleaseState = NotificationReleaseState.Released,
                Version      = "1.8.3",
                ContentHash  = NotificationContentHash.Of(announcementBody),
            },
            title: "Two API operation IDs were renamed",
            body: announcementBody,
            appVersionId: currentVersion?.Id,
            // #319: every language at once. The English above stays the notification's own text — the
            // content hash is taken over it, and the read path falls back to it — so only the other
            // languages become translation rows.
            translations: Quotinator.Api.Startup.NotificationTranslations.Build(
                app.Services.GetRequiredService<IApiLocalizer>(),
                ApiMessages.NotificationOperationIdRenameTitle,
                ApiMessages.NotificationOperationIdRenameBody));
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
        IApiLocalizer localizer = app.Services.GetRequiredService<IApiLocalizer>();

        await NotificationSeeding.SeedOnceAsync(
            app.Services.GetRequiredService<INotificationReader>(),
            app.Services.GetRequiredService<INotificationWriter>(),
            NotificationType.ActionRequired,
            new SchemaVersionOvershootMetadataDto
            {
                DataSchemaVersion = dbInitializer.DataSchemaVersion,
                AppSchemaVersion  = dbInitializer.SchemaVersion,
                // Not about a release at all — this describes the database's own recorded state, which
                // no version number characterises. Said outright rather than borrowing the running
                // version, which would also make the same unresolved overshoot re-announce itself on
                // every upgrade.
                ReleaseState = NotificationReleaseState.NotApplicable,
            },
            // #319: both recorded versions are substituted into each language's own template, so the
            // numbers are not embedded in prose written once in English. The structured values stay in
            // the metadata payload above — this is the same pair, rendered.
            title: Quotinator.Api.Startup.NotificationTranslations.Original(
                       localizer, ApiMessages.NotificationSchemaOvershootTitle),
            body: Quotinator.Api.Startup.NotificationTranslations.Original(
                       localizer, ApiMessages.NotificationSchemaOvershootBody,
                       dbInitializer.DataSchemaVersion, dbInitializer.SchemaVersion),
            dismissTrigger: NotificationDismissTrigger.DatabaseReset,
            appVersionId: currentVersion?.Id,
            translations: Quotinator.Api.Startup.NotificationTranslations.Build(
                localizer,
                ApiMessages.NotificationSchemaOvershootTitle,
                ApiMessages.NotificationSchemaOvershootBody,
                bodyArgs: [dbInitializer.DataSchemaVersion, dbInitializer.SchemaVersion]));
    }
    catch (Exception ex)
    {
        app.Services.GetRequiredService<ILogger<Program>>()
            .LogWarning(ex, "[Server] Failed to seed the #289 schema-version-overshoot notification — non-fatal, startup continues.");
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
            IChangelogReader changelogReader = app.Services.GetRequiredService<IChangelogReader>();
            ChangelogDocument? whatsNewDocument = await changelogReader.GetDocumentAsync(null);
            // #319: the other languages' documents, each checked against what was actually asked for.
            Dictionary<string, ChangelogDocument> translatedDocuments =
                await Quotinator.Api.Startup.WhatsNewNotification.LoadTranslatedDocumentsAsync(changelogReader);
            await Quotinator.Api.Startup.WhatsNewNotification.SeedAsync(
                app.Services.GetRequiredService<INotificationReader>(),
                app.Services.GetRequiredService<INotificationWriter>(),
                whatsNewDocument,
                lastActiveVersion,
                versionService.Version,
                currentVersion?.Id,
                app.Services.GetRequiredService<IApiLocalizer>(),
                translatedDocuments);
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
List<string> readyAddresses = [.. app.Services
    .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
    .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()
    ?.Addresses ?? []];
startupLog.LogReady(readyAddresses);

await app.WaitForShutdownAsync();

// Exposes Program to WebApplicationFactory<Program> in the test project.
public partial class Program
{
    private static readonly string[] SupportedCultures = ["en-GB", "de", "nl"];
}

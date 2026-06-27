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
using Quotinator.Core.Data;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Helpers;
using Quotinator.Api.Middleware;
using Quotinator.Data.Import;
using Quotinator.Data.Paths;
using Quotinator.Data.Repositories;
using Quotinator.Changelog.Services;
using Quotinator.Core.Services;
using Scalar.AspNetCore;
using Toolbelt.Blazor.Extensions.DependencyInjection;

DapperConfiguration.Configure();

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
            new() { Name = ApiTags.System,  Description = "Endpoints for monitoring and verifying the health of the API." },
            new() { Name = ApiTags.Quotes,  Description = "Endpoints for fetching and searching quotes." },
            new() { Name = ApiTags.Admin,   Description = "Administrative endpoints for database maintenance. Require `X-Api-Key` authentication. Protected by a concurrency-1 limiter — only one operation runs at a time; any concurrent request receives `429 Too Many Requests` immediately." },
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

    options.AddOperationTransformer((operation, context, cancellationToken) =>
    {
        if (operation.Tags?.Any(t => t.Name == ApiTags.Admin) == true)
        {
            operation.Security = new List<OpenApiSecurityRequirement>
            {
                new() { [new OpenApiSecuritySchemeReference("ApiKey")] = new List<string>() }
            };
        }
        return Task.CompletedTask;
    });

    // ── Year parameter schema fix ──────────────────────────────────────────────
    // yearFrom / yearTo / year / decade are declared as string? in the handler
    // signatures so that invalid values (e.g. "1980x") are caught by TryParseYear
    // and returned as a 422 at the point of origin rather than propagating as an
    // unhandled BadHttpRequestException through the entire middleware stack.
    //
    // The downside: the OpenAPI generator sees string? and emits type:string in the
    // spec, which is wrong — callers and tooling would infer the wrong input type.
    // This transformer patches only the three quote-filter endpoints back to integer,
    // keeping the spec accurate without reverting the error-handling approach.
    //
    // Scoped explicitly to the three paths that use TryParseYear. Any future endpoint
    // that genuinely accepts a string year value must NOT be added to this set.
    options.AddOperationTransformer((operation, context, cancellationToken) =>
    {
        var yearFilterPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "api/v1/quotes",
            "api/v1/quotes/random",
            "api/v1/quotes/search",
        };

        if (!yearFilterPaths.Contains(context.Description.RelativePath ?? string.Empty))
            return Task.CompletedTask;

        var yearParamNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "yearFrom", "yearTo", "year", "decade" };

        foreach (var param in operation.Parameters ?? [])
        {
            if (param.Name is not null && yearParamNames.Contains(param.Name)
                && param is OpenApiParameter p && p.Schema is OpenApiSchema s)
                s.Type = JsonSchemaType.Integer | JsonSchemaType.Null;
        }
        return Task.CompletedTask;
    });
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
// duplicateResolution section overrides this when present.
static DuplicateResolutionPolicy ParseResolutionPolicy(string? value) =>
    value?.ToLowerInvariant() == "overwrite"
        ? DuplicateResolutionPolicy.Overwrite
        : DuplicateResolutionPolicy.Skip;

static DuplicateResolutionPolicy? ParseNullableResolutionPolicy(string? value) =>
    value?.ToLowerInvariant() switch
    {
        "overwrite" => DuplicateResolutionPolicy.Overwrite,
        "skip"      => DuplicateResolutionPolicy.Skip,
        _           => null
    };

var configPolicy = new ManifestPolicy(
    Default:      ParseResolutionPolicy(builder.Configuration["Quotinator:DuplicateResolution:Default"]),
    Quotes:       ParseNullableResolutionPolicy(builder.Configuration["Quotinator:DuplicateResolution:Quotes"]),
    Sources:      ParseNullableResolutionPolicy(builder.Configuration["Quotinator:DuplicateResolution:Sources"]),
    Characters:   ParseNullableResolutionPolicy(builder.Configuration["Quotinator:DuplicateResolution:Characters"]),
    People:       ParseNullableResolutionPolicy(builder.Configuration["Quotinator:DuplicateResolution:People"]),
    Translations: ParseNullableResolutionPolicy(builder.Configuration["Quotinator:DuplicateResolution:Translations"]));

// Bundled sources are always read from the Docker image (AppContext.BaseDirectory/data/sources/).
// No file copy to the persistent volume is needed — only the database and DataProtection keys
// need to be on a writable, persistent path.
var bundledSourcesDir = Path.Combine(AppContext.BaseDirectory, "data", DataPaths.SourcesFolder);

// User imports: optional directory in the data volume. Create it so users can drop files in.
var importsDir = Path.Combine(dataDir, DataPaths.ImportsFolder);

static IReadOnlyList<SeedBatch> BuildSeedBatches(
    string bundledDir, string importsDir, ManifestPolicy configPolicy, ILogger<Program> log)
{
    var batches = new List<SeedBatch>();

    if (Directory.Exists(bundledDir))
    {
        var (files, policy) = OrderedByManifest(bundledDir, configPolicy, log);
        if (files.Count > 0)
            batches.Add(new SeedBatch(files, policy, "bundled sources"));
    }
    else
    {
        log.LogWarning("[Database - Init] bundled sources directory not found at {Dir} — database will be empty on first run", bundledDir);
    }

    if (Directory.Exists(importsDir))
    {
        var (files, policy) = OrderedByManifest(importsDir, configPolicy, log);
        if (files.Count > 0)
            batches.Add(new SeedBatch(files, policy, "user imports"));
    }

    return batches;
}

static (IReadOnlyList<string> Files, ManifestPolicy Policy) OrderedByManifest(
    string dir, ManifestPolicy configPolicy, ILogger<Program> log)
{
    var allJson = Directory.GetFiles(dir, "*.json")
                           .Where(f => !Path.GetFileName(f).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                           .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                           .ToList();

    var manifestPath = Path.Combine(dir, "manifest.json");
    if (!File.Exists(manifestPath))
    {
        log.LogInformation("[Database - Init] no manifest in {Dir} — importing {Count} JSON file(s) in alphabetical order", dir, allJson.Count);
        return (allJson, configPolicy);
    }

    try
    {
        var root              = JsonNode.Parse(File.ReadAllText(manifestPath));
        var manifestPolicyNode = root?["duplicateResolution"];
        var fromManifest      = manifestPolicyNode is null ? null : ParseManifestPolicyNode(manifestPolicyNode);
        var resolvedPolicy    = ManifestPolicy.Resolve(fromManifest, configPolicy);

        var listed = (root?["files"]?.AsArray() ?? [])
            .Select(e => Path.Combine(dir, e!["file"]!.GetValue<string>()))
            .Where(File.Exists)
            .ToList();

        var listedSet = new HashSet<string>(listed, StringComparer.OrdinalIgnoreCase);
        var unlisted  = allJson.Where(f => !listedSet.Contains(f)).ToList();
        if (unlisted.Count > 0)
            log.LogInformation("[Database - Init] {Count} file(s) not listed in manifest will be appended: {Files}",
                unlisted.Count, string.Join(", ", unlisted.Select(Path.GetFileName)));

        return ([.. listed, .. unlisted], resolvedPolicy);
    }
    catch (Exception ex)
    {
        log.LogWarning(ex, "[Database - Init] failed to read manifest at {Path} — falling back to alphabetical order", manifestPath);
        return (allJson, configPolicy);
    }
}

static ManifestPolicy ParseManifestPolicyNode(JsonNode node)
{
    static DuplicateResolutionPolicy ParsePol(JsonNode? n) =>
        n?.GetValue<string>().ToLowerInvariant() == "overwrite"
            ? DuplicateResolutionPolicy.Overwrite
            : DuplicateResolutionPolicy.Skip;

    static DuplicateResolutionPolicy? ParseNullPol(JsonNode? n) =>
        n?.GetValue<string>().ToLowerInvariant() switch
        {
            "overwrite" => DuplicateResolutionPolicy.Overwrite,
            "skip"      => DuplicateResolutionPolicy.Skip,
            _           => null
        };

    return new ManifestPolicy(
        Default:      ParsePol(node["default"]),
        Quotes:       ParseNullPol(node["quotes"]),
        Sources:      ParseNullPol(node["sources"]),
        Characters:   ParseNullPol(node["characters"]),
        People:       ParseNullPol(node["people"]),
        Translations: ParseNullPol(node["translations"]));
}

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

// Resolve seed batches before building the host — uses the early logger factory so errors
// surface before the DI container starts up. Batches are captured into the lambda closure.
var earlyLoggerFactory = LoggerFactory.Create(b => b.AddConsole());
var earlyLogger        = earlyLoggerFactory.CreateLogger<Program>();
var seedBatches        = BuildSeedBatches(bundledSourcesDir, importsDir, configPolicy, earlyLogger);
builder.Services.AddSingleton<IImportBatchRepository, SqliteImportBatchRepository>();
builder.Services.AddSingleton<IDatabaseInitializer>(sp => new DatabaseInitializer(
    connectionFactory, dbOptions, QuotinatorMigrations.All, seedBatches,
    sp.GetRequiredService<IImportBatchRepository>(),
    sp.GetRequiredService<ILogger<DatabaseInitializer>>()));
builder.Services.AddSingleton<IQuoteService>(_ => new SqliteQuoteService(connectionFactory));
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

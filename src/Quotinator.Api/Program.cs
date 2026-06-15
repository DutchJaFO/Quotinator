using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;
using Quotinator.Api.Components;
using Quotinator.Api.Endpoints;
using Quotinator.Constants;
using Quotinator.Core.Data;
using Quotinator.Core.Data.TypeHandlers;
using Quotinator.Data.Data;
using Quotinator.Core.Services;
using Scalar.AspNetCore;
using Toolbelt.Blazor.Extensions.DependencyInjection;

DapperConfiguration.Configure();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Tags = new HashSet<OpenApiTag>
        {
            new() { Name = ApiTags.System,  Description = "Endpoints for monitoring and verifying the health of the API." },
            new() { Name = ApiTags.Quotes,  Description = "Endpoints for fetching and searching quotes." },
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

// Data path — configurable so the HA add-on can point this at /data (the supervisor's
// persistent volume) while standalone Docker keeps the default /app/data.
// The HA supervisor sets Quotinator__DataPath via config.yaml env_vars. When that env var
// is absent (e.g. HA caches an older config), fall back to /data if it is already a mounted
// volume (writable directory owned by the HA supervisor), so DataProtection keys are always
// on a persistent volume rather than the ephemeral container filesystem.
static string? HaFallbackPath()
{
    const string haData = "/data";
    try { return Directory.Exists(haData) ? Path.Combine(haData, "quotes.json") : null; }
    catch { return null; }
}
var dataPath = builder.Configuration["Quotinator:DataPath"]
    ?? HaFallbackPath()
    ?? Path.Combine(AppContext.BaseDirectory, "data", "quotes.json");
var dataDir = Path.GetDirectoryName(dataPath) ?? Path.Combine(AppContext.BaseDirectory, "data");
Directory.CreateDirectory(dataDir);

// First-run seed: if the configured data path doesn't exist yet (e.g. a fresh HA add-on
// data volume) copy the bundled quotes.json from the image into the persistent directory.
var bundledData = Path.Combine(AppContext.BaseDirectory, "data", "quotes.json");
if (!File.Exists(dataPath) && File.Exists(bundledData))
    File.Copy(bundledData, dataPath);

// Persist DataProtection keys to a subdirectory of the data volume so antiforgery tokens
// and Blazor circuit descriptors survive container restarts and add-on updates.
var keysDir = Path.Combine(dataDir, ".keys");
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

builder.Services.AddProblemDetails();
builder.Services.AddSingleton<IVersionService, VersionService>();
builder.Services.AddSingleton<IChangelogService, ChangelogService>();

var dbPath = Path.Combine(dataDir, "quotes.db");
var connectionFactory = new SqliteConnectionFactory(dbPath);
builder.Services.AddSingleton<IDbConnectionFactory>(_ => connectionFactory);
builder.Services.AddSingleton<IDatabaseInitializer>(sp => new DatabaseInitializer(
    connectionFactory, dataPath, sp.GetRequiredService<ILogger<DatabaseInitializer>>()));
builder.Services.AddSingleton<IQuoteService>(_ => new SqliteQuoteService(connectionFactory));
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

// Map HA log level names (trace/debug/info/notice/warning/error/fatal) to ASP.NET Core LogLevel.
// HA uses "info"; ASP.NET Core uses "Information" — the mapping is required, they do not match.
// Per-category overrides in appsettings.json (Microsoft.AspNetCore: Warning) remain effective
// because a specific category prefix always wins over the global minimum.
var haLogLevel = builder.Configuration["Quotinator:LogLevel"] ?? "info";
builder.Logging.SetMinimumLevel(haLogLevel.ToLowerInvariant() switch
{
    "trace"   => LogLevel.Trace,
    "debug"   => LogLevel.Debug,
    "notice"  => LogLevel.Information,
    "info"    => LogLevel.Information,
    "warning" => LogLevel.Warning,
    "error"   => LogLevel.Error,
    "fatal"   => LogLevel.Critical,
    _         => LogLevel.Information
});

var app = builder.Build();

var dbInitializer = app.Services.GetRequiredService<IDatabaseInitializer>();
await dbInitializer.InitialiseAsync();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
var versionService = app.Services.GetRequiredService<IVersionService>();
var logRequests = app.Configuration.GetValue<bool>("Quotinator:LogRequests");

var banner = new System.Text.StringBuilder()
    .AppendLine("############################################")
    .AppendLine($"Quotinator v{versionService.Version} starting")
    .AppendLine($"Data:  {dataPath}")
    .AppendLine($"DB:    schema v{dbInitializer.SchemaVersion} — {dbInitializer.QuoteCount} quotes  {dbInitializer.SourceCount} sources  {dbInitializer.CharacterCount} characters  {dbInitializer.PeopleCount} people")
    .AppendLine($"Keys:  {keysDir}")
    .Append($"Cfg:   log_level={haLogLevel}  log_requests={(logRequests ? "on" : "off")}  ssl={( sslEnabled ? "on" : "off")}");
if (sslEnabled)
    banner.AppendLine().Append($"SSL:   {sslCertFile}  /  {sslKeyFile}");
banner.AppendLine().Append("############################################");

logger.LogInformation("{Banner}", banner);

var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
    logger.LogInformation("Quotinator v{Version} stopping", versionService.Version));

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

// Optional request logging for /api/v1/quotes/* — off by default so the supervisor log
// stays clean. Enable with log_requests: true in the add-on config (or Quotinator__LogRequests=true).
// Uses a dedicated logger category so it can be suppressed independently if needed.
if (logRequests)
{
    var requestLogger = app.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Quotinator.Requests");

    app.Use(async (context, next) =>
    {
        if (!context.Request.Path.StartsWithSegments("/api/v1/quotes"))
        {
            await next();
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await next();
        sw.Stop();

        requestLogger.LogInformation("{Method} {Path}{Query} → {Status} in {Ms}ms",
            context.Request.Method,
            context.Request.Path,
            context.Request.QueryString.Value,
            context.Response.StatusCode,
            sw.ElapsedMilliseconds);
    });
}

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

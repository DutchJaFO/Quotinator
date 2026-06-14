using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;
using Quotinator.Api;
using Quotinator.Api.Components;
using Quotinator.Api.Endpoints;
using Quotinator.Api.Services;
using Quotinator.Core.Services;
using Scalar.AspNetCore;
using Toolbelt.Blazor.Extensions.DependencyInjection;

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

// Data path drives both the quote dataset and the DataProtection key directory.
var dataPath = builder.Configuration["Quotinator:DataPath"]
    ?? Path.Combine(AppContext.BaseDirectory, "data", "quotes.json");
var dataDir = Path.GetDirectoryName(dataPath) ?? Path.Combine(AppContext.BaseDirectory, "data");
Directory.CreateDirectory(dataDir);

// Persist DataProtection keys so antiforgery tokens (and Blazor circuit descriptors)
// survive container restarts. Keys live alongside quotes.json in the data volume.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataDir));

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
        // ASPNETCORE_HTTP_PORTS in addon/config.yaml also binds 8099; this call is a no-op
        // for standalone Docker where that env var is cleared in the Dockerfile.
        kestrel.ListenAnyIP(8099);

        if (sslEnabled && File.Exists(sslCertFile) && File.Exists(sslKeyFile))
            kestrel.ListenAnyIP(8080, lo => lo.UseHttps(sslCertFile, sslKeyFile));
        else
            kestrel.ListenAnyIP(8080);
    });
}

builder.Services.AddProblemDetails();
builder.Services.AddSingleton<IVersionService, VersionService>();

builder.Services.AddSingleton<IQuoteService>(_ => new QuoteService(dataPath));
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

var app = builder.Build();

// Eager init — resolves the path and loads quotes before the first request.
// The count appears in both the application log and the VS Debug output window.
var quoteService = app.Services.GetRequiredService<IQuoteService>();
var quoteCount   = quoteService.GetAll(1, 1).TotalCount;
Console.WriteLine($"[Quotinator] Data: {dataPath}");
Console.WriteLine($"[Quotinator] Quotes loaded: {quoteCount}");
app.Logger.LogInformation("Loaded {Count} quotes from {Path}", quoteCount, dataPath);

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

app.MapOpenApi();
app.MapScalarApiReference();

app.UseAntiforgery();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/api/v1/health", () => Results.Ok(new { status = "healthy" }))
   .WithName("Health")
   .WithTags(ApiTags.System)
   .WithSummary("Health check")
   .WithDescription("Returns the current health status of the API. Use this endpoint to verify the service is running.");

app.MapGet("/api/v1/version", (IVersionService vs, IWebHostEnvironment env) =>
    Results.Ok(new { version = vs.Version, environment = env.EnvironmentName }))
   .WithName("Version")
   .WithTags(ApiTags.System)
   .WithSummary("API version")
   .WithDescription("Returns the running version and environment.");

app.MapQuoteEndpoints();

// Sets or clears the UI language cookie and redirects back. LocalRedirect prevents open-redirect attacks.
// Empty culture = auto-detect mode: deletes the cookie so Accept-Language takes over.
// Non-empty culture: sets the cookie (c={culture}|uic={culture}) read by CookieRequestCultureProvider.
app.MapGet("/Culture/Set", (string? culture, string redirectUri, HttpContext context) =>
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
});

app.Run();

// Exposes Program to WebApplicationFactory<Program> in the test project.
public partial class Program { }

using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
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
                "Write endpoints, authentication, and MCP support are planned for v2/v3.",
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

builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<IVersionService, VersionService>();
var dataPath = builder.Configuration["Quotinator:DataPath"]
    ?? Path.Combine(AppContext.BaseDirectory, "data", "quotes.json");

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

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRequestLocalization();
app.UseRateLimiter();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseHttpsRedirection();
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
            new CookieOptions { SameSite = SameSiteMode.Lax, Secure = true });
    }
    else
    {
        context.Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture, culture)),
            new CookieOptions { MaxAge = TimeSpan.FromDays(365), IsEssential = true, SameSite = SameSiteMode.Lax, Secure = true });
    }
    return TypedResults.LocalRedirect(redirectUri);
});

app.Run();

// Exposes Program to WebApplicationFactory<Program> in the test project.
public partial class Program { }

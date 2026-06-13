using Microsoft.AspNetCore.Localization;
using Microsoft.OpenApi;
using Quotinator.Api;
using Quotinator.Api.Components;
using Quotinator.Api.Services;
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
            new() { Name = ApiTags.Quotes,  Description = "Endpoints for fetching and searching movie quotes." },
        };

        document.Info = new()
        {
            Title = "Quotinator API",
            Version = "v1",
            Description =
                "A self-hosted movie quote REST API. Serves real, verified movie quotes " +
                "from a curated dataset seeded from MIT-licensed sources.\n\n" +
                "**v1 scope:** read-only endpoints for fetching and searching quotes. " +
                "Write endpoints, authentication, and MCP support are planned for v2/v3.",
            Contact = new() { Name = "GitHub", Url = new Uri("https://github.com/DutchJaFO/Quotinator") }
        };
        return Task.CompletedTask;
    });
});

builder.Services.AddProblemDetails();
builder.Services.AddSingleton<IVersionService, VersionService>();
builder.Services.AddI18nText();

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supported = new[] { "en", "en-GB", "de", "nl" };
    options.DefaultRequestCulture = new RequestCulture("en");
    options.AddSupportedCultures(supported);
    options.AddSupportedUICultures(supported);
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRequestLocalization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

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
   .WithDescription("Returns the running version and environment. The Blazor UI uses this endpoint to display version information.");

app.Run();

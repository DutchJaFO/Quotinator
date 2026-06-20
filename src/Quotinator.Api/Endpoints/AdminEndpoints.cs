using Quotinator.Api.Endpoints.Filters;
using Quotinator.Constants.Api;
using Quotinator.Constants.RateLimiting;
using Quotinator.Core.Data;

namespace Quotinator.Api.Endpoints;

/// <summary>Registers all <c>/api/v1/admin</c> endpoints.</summary>
internal static class AdminEndpoints
{
    internal static void MapAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin")
                       .WithTags(ApiTags.Admin)
                       .RequireRateLimiting(RateLimitPolicies.Admin)
                       .AddEndpointFilter<AdminApiKeyFilter>();

        group.MapGet("/database/seed/preview", async (IDatabaseInitializer db) =>
        {
            var preview = await db.PreviewSeedAsync();
            return Results.Ok(new
            {
                files              = preview.Files.Select(f => new { f.FileName, f.QuoteCount }),
                totalQuotes        = preview.TotalQuotes,
                uniqueQuotes       = preview.UniqueQuotes,
                crossFileDuplicates = preview.CrossFileDuplicates.Select(d => new
                {
                    d.EntityType,
                    d.Id,
                    d.Label,
                    d.FirstSeenInFile,
                    d.ConflictFile,
                    appliedPolicy = d.AppliedPolicy.ToString().ToLowerInvariant()
                })
            });
        })
        .WithName("PreviewSeed")
        .WithSummary("Preview seed import")
        .WithDescription(
            "Scans all configured source files without touching the database. " +
            "Returns the quote count per file, total and unique quote counts, and any cross-file duplicate IDs with the policy that would be applied. " +
            "Use this before calling `reseed` to understand what will be imported. " +
            "Requires `Authorization: Bearer <key>` matching `Quotinator:AdminApiKey`. Returns `401` if the key is not configured or does not match.");

        group.MapPost("/database/reseed", async (IDatabaseInitializer db) =>
        {
            await db.ReseedAsync();
            return Results.Ok(new
            {
                quotes      = db.QuoteCount,
                sources     = db.SourceCount,
                characters  = db.CharacterCount,
                people      = db.PeopleCount,
                duplicates  = db.LastSeedDuplicates.Count
            });
        })
        .WithName("ReseedDatabase")
        .WithSummary("Reseed the database")
        .WithDescription(
            "Clears all data tables and reimports every quote from the configured source files. " +
            "The schema version history is preserved — no migrations are re-applied. " +
            "Returns the row counts and duplicate count after the operation completes. " +
            "Protected by a concurrency-1 limiter — a second call while one is in progress receives `429 Too Many Requests` immediately. " +
            "Requires `Authorization: Bearer <key>` matching `Quotinator:AdminApiKey`. Returns `401` if the key is not configured or does not match.");

        group.MapPost("/database/reset", async (IDatabaseInitializer db) =>
        {
            await db.ResetAsync();
            return Results.Ok(new
            {
                quotes     = db.QuoteCount,
                sources    = db.SourceCount,
                characters = db.CharacterCount,
                people     = db.PeopleCount,
                duplicates = db.LastSeedDuplicates.Count
            });
        })
        .WithName("ResetDatabase")
        .WithSummary("Reset the database")
        .WithDescription(
            "Clears all data and schema version history, reapplies all migrations from scratch, " +
            "then reimports every quote from the configured source files. " +
            "Equivalent to deleting the database file and restarting. " +
            "Returns the row counts and duplicate count after the operation completes. " +
            "Protected by a concurrency-1 limiter — a second call while one is in progress receives `429 Too Many Requests` immediately. " +
            "Requires `Authorization: Bearer <key>` matching `Quotinator:AdminApiKey`. Returns `401` if the key is not configured or does not match.");
    }
}

using Quotinator.Api.Endpoints.Filters;
using Quotinator.Constants.Api;
using Quotinator.Constants.RateLimiting;
using Quotinator.Data.Database;
using Quotinator.Data.Repositories;

namespace Quotinator.Api.Endpoints;

/// <summary>Registers all <c>/api/v1/admin</c> endpoints.</summary>
internal static class AdminEndpoints
{
    internal static void MapAdminEndpoints(this WebApplication app)
    {
        // Non-destructive endpoints — read-only; no API key required.
        var publicGroup = app.MapGroup("/api/v1/admin")
                             .WithTags(ApiTags.Admin)
                             .RequireRateLimiting(RateLimitPolicies.Admin);

        // Destructive or sensitive endpoints — require X-Api-Key header.
        var adminGroup = app.MapGroup("/api/v1/admin")
                            .WithTags(ApiTags.Admin)
                            .RequireRateLimiting(RateLimitPolicies.Admin)
                            .AddEndpointFilter<AdminApiKeyFilter>();

        // ── Public ────────────────────────────────────────────────────────────

        publicGroup.MapGet("/database/seed/preview", async (IDatabaseInitializer db) =>
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
            "Use this before calling `reseed` to understand what will be imported.");

        publicGroup.MapGet("/audit", async (
            string? table,
            string? recordId,
            int page     = 1,
            int pageSize = 50,
            IAuditReader auditReader = null!) =>
        {
            if (page < 1)       page     = 1;
            if (pageSize < 1)   pageSize = 1;
            if (pageSize > 200) pageSize = 200;

            var result = await auditReader.GetPagedAsync(table, recordId, page, pageSize);

            return Results.Ok(new
            {
                totalMatching = result.TotalCount,
                totalPages    = result.TotalPages,
                page          = result.Page,
                pageSize      = result.PageSize,
                items         = result.Items
            });
        })
        .WithName("GetAuditLog")
        .WithSummary("Get audit log")
        .WithDescription(
            "Returns a paginated list of audit entries, newest first. " +
            "Filter by `table` (e.g. `Quotes`, `Database`) and/or `recordId` (Guid). " +
            "Maximum `pageSize` is 200.");

        // ── Admin-only ────────────────────────────────────────────────────────

        adminGroup.MapPost("/database/reseed", async (IDatabaseInitializer db) =>
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
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`. Returns `401` if the key is not configured or does not match.");

        adminGroup.MapPost("/database/reset", async (IDatabaseInitializer db) =>
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
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`. Returns `401` if the key is not configured or does not match.");

        adminGroup.MapDelete("/audit", async (string? table, IAuditWriter auditWriter) =>
        {
            await auditWriter.ClearAsync(table);
            return Results.NoContent();
        })
        .WithName("ClearAuditLog")
        .WithSummary("Clear audit log")
        .WithDescription(
            "Deletes all audit entries, or only entries for a specific table when `table` is supplied. " +
            "A single audit entry recording the purge is written after the delete so there is always a trace that a clear occurred. " +
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`. Returns `401` if the key is not configured or does not match.");
    }
}

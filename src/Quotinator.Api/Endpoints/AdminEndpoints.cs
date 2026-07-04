using Quotinator.Api.Endpoints.Filters;
using Quotinator.Constants.Api;
using Quotinator.Constants.RateLimiting;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Import;
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

        publicGroup.MapGet("/database/seed/preview", async (IDatabaseInitializer db, IApiLocalizer localizer) =>
        {
            var preview = await db.PreviewSeedAsync();
            return Results.Ok(new
            {
                files = preview.Files.Select(f => new
                {
                    f.FileName,
                    f.QuoteCount,
                    refreshOutcome     = f.RefreshOutcome?.ToString().ToLowerInvariant(),
                    lastRefreshedAtUtc = f.LastRefreshedAtUtc,
                    issue              = f.Issue?.ToString().ToLowerInvariant(),
                    message            = f.Issue switch
                    {
                        SeedFileIssue.Missing     => localizer[ApiMessages.SeedFileMissing],
                        SeedFileIssue.InvalidJson => localizer[ApiMessages.SeedFileInvalidJson],
                        _                         => null
                    }
                }),
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
            "For a file with a `downloadUrl`, also returns `refreshOutcome` (`updated`, `uptodate`, `failed`, or `skippedcollision`) and " +
            "`lastRefreshedAtUtc` (the cached copy's own last-write time, not \"now\") — both omitted for a file with no `downloadUrl`. " +
            "`issue` (`missing` or `invalidjson`) and a localised `message` (following `Accept-Language`, like all other API error text) are present " +
            "when the file could not be parsed at all — the only way to tell a `quoteCount` of `0` caused by a genuine parse error apart from a file " +
            "that is simply, validly empty. Applies to every file, not only those with a `downloadUrl`. A `quoteCount` of `0` alongside a " +
            "`failed`/`skippedcollision` `refreshOutcome` means the cache is currently degraded and fell back to the original file. " +
            "Use this before calling `reseed` to understand what will be imported.");

        publicGroup.MapGet("/audit", async (
            string? table,
            string? recordId,
            int page     = 1,
            int pageSize = 50,
            ISystemAuditReader auditReader = null!) =>
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

        adminGroup.MapPost("/database/reseed", async (IDatabaseInitializer db, bool forceSourceRefresh = false) =>
        {
            await db.ReseedAsync(forceSourceRefresh);
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
            "Auto-updated sources are refreshed from the network first if stale (or unconditionally when `forceSourceRefresh=true`), " +
            "unless `Quotinator:AutoUpdateSources` is `false`, in which case `forceSourceRefresh` has no effect. " +
            "Returns the row counts and duplicate count after the operation completes. " +
            "Protected by a concurrency-1 limiter — a second call while one is in progress receives `429 Too Many Requests` immediately. " +
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`. Returns `401` if the key is not configured or does not match.");

        adminGroup.MapPost("/database/reset", async (IDatabaseInitializer db, bool preserveSchemaVersion = false, bool forceSourceRefresh = false) =>
        {
            await db.ResetAsync(preserveSchemaVersion, forceSourceRefresh);
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
            "Clears all data, reapplies all migrations from scratch, " +
            "then reimports every quote from the configured source files. " +
            "Equivalent to deleting the database file and restarting. " +
            "The audit log (`System_AuditEntries`) always survives a reset — clear it separately via `DELETE /api/v1/admin/audit` if needed. " +
            "By default, schema migration history is also cleared and replayed; pass `preserveSchemaVersion=true` to keep the existing migration history instead. " +
            "Auto-updated sources are refreshed from the network first if stale (or unconditionally when `forceSourceRefresh=true`), " +
            "unless `Quotinator:AutoUpdateSources` is `false`, in which case `forceSourceRefresh` has no effect. " +
            "Returns the row counts and duplicate count after the operation completes. " +
            "Protected by a concurrency-1 limiter — a second call while one is in progress receives `429 Too Many Requests` immediately. " +
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`. Returns `401` if the key is not configured or does not match.");

        adminGroup.MapPost("/sources/refresh", async (IDatabaseInitializer db, bool force = false) =>
        {
            var resolution = await db.RefreshSourcesAsync(force);
            return Results.Ok(new
            {
                results = resolution.Results.Select(r => new
                {
                    r.Name,
                    r.Url,
                    outcome = r.Outcome.ToString().ToLowerInvariant(),
                    r.Detail,
                    lastRefreshedAtUtc = r.LastRefreshedAtUtc
                })
            });
        })
        .WithName("RefreshSources")
        .WithSummary("Refresh downloaded source caches")
        .WithDescription(
            "Refreshes the internal and external download caches for every manifest entry that declares a `downloadUrl`/`github`, " +
            "without touching the database — the reimport itself only happens on the next reseed/reset/startup. " +
            "Stale or missing entries are downloaded; fresh entries are left as-is unless `force=true`. " +
            "Has no effect when `Quotinator:AutoUpdateSources` is `false`. " +
            "Each result includes `lastRefreshedAtUtc` — the effective cache file's own last-write time, so an `uptodate` outcome " +
            "still shows exactly how old the cached copy is rather than only that it was within the TTL window. `null` when no trusted cache file exists (e.g. a collision). " +
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`. Returns `401` if the key is not configured or does not match.");

        adminGroup.MapDelete("/audit", async (string? table, ISystemAuditWriter auditWriter) =>
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

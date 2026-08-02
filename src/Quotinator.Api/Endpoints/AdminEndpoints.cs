using Quotinator.Data.Enums;
using System.ComponentModel;
using Microsoft.AspNetCore.Mvc;
using Quotinator.Api.Endpoints.Filters;
using Quotinator.Api.Endpoints.Shared;
using Quotinator.Constants.Api;
using Quotinator.Constants.RateLimiting;
using Quotinator.Core.Models;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
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
                            .AddEndpointFilter<AdminApiKeyFilter>()
                            .WithMetadata(AdminApiKeyRequiredMarker.Instance);

        // ── Public ────────────────────────────────────────────────────────────

        publicGroup.MapGet("/database/seed/preview", async (IDatabaseInitializer db, IApiLocalizer localizer) =>
        {
            var preview = await db.PreviewSeedAsync();
            return Results.Ok(new SeedPreviewResponse
            {
                Files = preview.Files.Select(f => new Quotinator.Core.Models.SeedFilePreview
                {
                    FileName           = f.FileName,
                    QuoteCount         = f.QuoteCount,
                    RefreshOutcome     = f.RefreshOutcome?.ToString().ToLowerInvariant(),
                    LastRefreshedAtUtc = f.LastRefreshedAtUtc,
                    Issue              = f.Issue?.ToString().ToLowerInvariant(),
                    Message            = f.Issue switch
                    {
                        SeedFileIssue.Missing     => localizer[ApiMessages.SeedFileMissing],
                        SeedFileIssue.InvalidJson => localizer[ApiMessages.SeedFileInvalidJson],
                        _                         => null
                    }
                }).ToList(),
                Reports = preview.Reports
            });
        })
        .WithName("PreviewSeed")
        .WithSummary("Preview seed import")
        .Produces<SeedPreviewResponse>(StatusCodes.Status200OK)
        .WithDescription(
            "Scans all configured source files without writing anything to the database. " +
            "Returns the quote count per file, plus a per-file, per-entity-type report (new/modified/blocked/discarded/pending/stale " +
            "counts) computed by running the real import action planner read-only against the current database state (issue #221). " +
            "For a file with a `downloadUrl`, also returns `refreshOutcome` (`updated`, `uptodate`, `failed`, or `skippedcollision`) and " +
            "`lastRefreshedAtUtc` (the cached copy's own last-write time, not \"now\") — both omitted for a file with no `downloadUrl`. " +
            "`issue` (`missing` or `invalidjson`) and a localised `message` (following `Accept-Language`, like all other API error text) are present " +
            "when the file could not be parsed at all — the only way to tell a `quoteCount` of `0` caused by a genuine parse error apart from a file " +
            "that is simply, validly empty. Applies to every file, not only those with a `downloadUrl`. A `quoteCount` of `0` alongside a " +
            "`failed`/`skippedcollision` `refreshOutcome` means the cache is currently degraded and fell back to the original file. " +
            "Known limitation: since this preview never writes between files, a quote id appearing in two different files that are both " +
            "new to the database reports as `new` in both files' reports rather than `new` in one and `modified` in the other, unlike a " +
            "real seed run — always accurate against a database that already has the relevant rows. " +
            "Use this before calling `reseed` to understand what will be imported.");

        publicGroup.MapGet("/audit", async (
            string? table,
            string? recordId,
            IApiLocalizer localizer,
            [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
            [Description("Number of entries per page (0–500). 0 means every matching entry as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null,
            IAuditEntryReader auditReader = null!) =>
        {
            if (!PaginationParsing.TryParse(page, pageSize, localizer, out var pageValue, out var pageSizeValue, out var pageError))
                return pageError!;

            var result = await auditReader.GetPagedAsync(table, recordId, pageValue, pageSizeValue);

            return PaginationParsing.ValidatePageBeyondLast(pageValue, result.TotalPages, localizer)
                ?? Results.Ok(result);
        })
        .WithName("GetAuditLog")
        .WithSummary("Get audit log")
        .Produces<PagedItems<AuditEntryEntity>>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)
        .WithDescription(
            "Returns a paginated list of audit entries, newest first. " +
            "Filter by `table` (e.g. `Quotes`, `Database`) and/or `recordId` (Guid). " +
            "Maximum `pageSize` is 500.");

        // ── Admin-only ────────────────────────────────────────────────────────

        adminGroup.MapPost("/database/reseed", async (IDatabaseInitializer db, bool forceSourceRefresh = false) =>
        {
            await db.ReseedAsync(forceSourceRefresh);
            return Results.Ok(new DatabaseSeedSummaryResponse
            {
                Quotes          = db.QuoteCount,
                Sources         = db.SourceCount,
                Characters      = db.CharacterCount,
                People          = db.PeopleCount,
                Series          = db.SeriesCount,
                Universes       = db.UniverseCount,
                StageDirections = db.StageDirectionCount,
                SoundCues       = db.SoundCueCount,
                Conversations   = db.ConversationCount,
                Reports         = db.LastSeedReport
            });
        })
        .WithName("ReseedDatabase")
        .WithSummary("Reseed the database")
        .Produces<DatabaseSeedSummaryResponse>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
        .WithDescription(
            "Clears all data tables and reimports every quote from the configured source files. " +
            "The schema version history is preserved — no migrations are re-applied. " +
            "Auto-updated sources are refreshed from the network first if stale (or unconditionally when `forceSourceRefresh=true`), " +
            "unless `Quotinator:AutoUpdateSources` is `false`, in which case `forceSourceRefresh` has no effect. " +
            "Returns the row counts and a per-file, per-entity-type report (new/modified/blocked/discarded/pending/stale counts) " +
            "after the operation completes (issue #221). " +
            "Protected by a concurrency-1 limiter — a second call while one is in progress receives `429 Too Many Requests` immediately. " +
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`. Returns `401` if the key is not configured or does not match.");

        adminGroup.MapPost("/database/reset", async (IDatabaseInitializer db, Quotinator.Api.Startup.DatabaseHealthState dbHealth, bool preserveSchemaVersion = false, bool forceSourceRefresh = false) =>
        {
            await db.ResetAsync(preserveSchemaVersion, forceSourceRefresh);
            dbHealth.MarkHealthy();
            return Results.Ok(new DatabaseSeedSummaryResponse
            {
                Quotes          = db.QuoteCount,
                Sources         = db.SourceCount,
                Characters      = db.CharacterCount,
                People          = db.PeopleCount,
                Series          = db.SeriesCount,
                Universes       = db.UniverseCount,
                StageDirections = db.StageDirectionCount,
                SoundCues       = db.SoundCueCount,
                Conversations   = db.ConversationCount,
                Reports         = db.LastSeedReport
            });
        })
        .WithName("ResetDatabase")
        .WithSummary("Reset the database")
        .Produces<DatabaseSeedSummaryResponse>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
        .WithDescription(
            "Clears all data, reapplies all migrations from scratch, " +
            "then reimports every quote from the configured source files. " +
            "Equivalent to deleting the database file and restarting. " +
            "The audit log (`System_AuditEntries`) always survives a reset — clear it separately via `DELETE /api/v1/admin/audit` if needed. " +
            "By default, schema migration history is also cleared and replayed; pass `preserveSchemaVersion=true` to keep the existing migration history instead. " +
            "Auto-updated sources are refreshed from the network first if stale (or unconditionally when `forceSourceRefresh=true`), " +
            "unless `Quotinator:AutoUpdateSources` is `false`, in which case `forceSourceRefresh` has no effect. " +
            "Returns the row counts and a per-file, per-entity-type report (new/modified/blocked/discarded/pending/stale counts) " +
            "after the operation completes (issue #221). " +
            "Protected by a concurrency-1 limiter — a second call while one is in progress receives `429 Too Many Requests` immediately. " +
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`. Returns `401` if the key is not configured or does not match.");

        adminGroup.MapPost("/sources/refresh", async (IDatabaseInitializer db, bool force = false) =>
        {
            var resolution = await db.RefreshSourcesAsync(force);
            return Results.Ok(new SourceRefreshResponse
            {
                Results = resolution.Results.Select(r => new SourceRefreshResultResponse
                {
                    Name               = r.Name,
                    Url                = r.Url,
                    Outcome            = r.Outcome.ToString().ToLowerInvariant(),
                    Detail             = r.Detail,
                    LastRefreshedAtUtc = r.LastRefreshedAtUtc
                }).ToList()
            });
        })
        .WithName("RefreshSources")
        .WithSummary("Refresh downloaded source caches")
        .Produces<SourceRefreshResponse>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
        .WithDescription(
            "Refreshes the internal and external download caches for every manifest entry that declares a `downloadUrl`/`github`, " +
            "without touching the database — the reimport itself only happens on the next reseed/reset/startup. " +
            "Stale or missing entries are downloaded; fresh entries are left as-is unless `force=true`. " +
            "Has no effect when `Quotinator:AutoUpdateSources` is `false`. " +
            "Each result includes `lastRefreshedAtUtc` — the effective cache file's own last-write time, so an `uptodate` outcome " +
            "still shows exactly how old the cached copy is rather than only that it was within the TTL window. `null` when no trusted cache file exists (e.g. a collision). " +
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`. Returns `401` if the key is not configured or does not match.");

        adminGroup.MapDelete("/audit", async (string? table, IAuditEntryWriter auditWriter) =>
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

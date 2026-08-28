using Quotinator.Data.Enums;
using System.ComponentModel;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Quotinator.Api.Startup;
using Quotinator.Api.Endpoints.Filters;
using Quotinator.Api.Endpoints.Shared;
using Quotinator.Constants.Api;
using Quotinator.Constants.RateLimiting;
using Quotinator.Core.Models;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Helpers;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Data.Import;

namespace Quotinator.Api.Endpoints;

/// <summary>Registers all <c>/api/v1/admin</c> endpoints.</summary>
internal static class AdminEndpoints
{
    internal static void MapAdminEndpoints(this WebApplication app)
    {
        // Non-destructive endpoints — read-only; no API key required.
        RouteGroupBuilder publicGroup = app.MapGroup("/api/v1/admin")
                             .WithTags(ApiTags.Admin)
                             .RequireRateLimiting(RateLimitPolicies.Admin);

        // Destructive or sensitive endpoints — require X-Api-Key header.
        RouteGroupBuilder adminGroup = app.MapGroup("/api/v1/admin")
                            .WithTags(ApiTags.Admin)
                            .RequireRateLimiting(RateLimitPolicies.Admin)
                            .AddEndpointFilter<AdminApiKeyFilter>()
                            .WithMetadata(AdminApiKeyRequiredMarker.Instance);

        // ── Public ────────────────────────────────────────────────────────────

        publicGroup.MapGet("/database/seed/preview", async (IDatabaseInitializer db, IApiLocalizer localizer) =>
        {
            SeedPreviewResult preview = await db.PreviewSeedAsync();
            return Results.Ok(new SeedPreviewResponse
            {
                Files = [.. preview.Files.Select(f => new Quotinator.Core.Models.SeedFilePreview
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
                })],
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
            if (!PaginationParsing.TryParse(page, pageSize, localizer, out int pageValue, out int pageSizeValue, out IResult? pageError))
                return pageError!;

            PagedItems<AuditEntryEntity> result = await auditReader.GetPagedAsync(table, recordId, pageValue, pageSizeValue);
            PagedItems<AuditEntryResponse> mapped = new(
                [.. result.Items.Select(ToAuditEntryResponse)], result.Page, result.PageSize, result.TotalCount);

            return PaginationParsing.ValidatePageBeyondLast(pageValue, result.TotalPages, localizer)
                ?? Results.Ok(mapped);
        })
        .WithName("GetAuditLog")
        .WithSummary("Get audit log")
        .Produces<PagedItems<AuditEntryResponse>>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)
        .WithDescription(
            "Returns a paginated list of audit entries, newest first. " +
            "Filter by `table` (e.g. `Quotes`, `Database`) and/or `recordId` (Guid). " +
            "Maximum `pageSize` is 500.");

        publicGroup.MapGet("/audit/date-range", async (
            IAuditEntryReader auditReader,
            IChangeReader changeReader) =>
        {
            (DateTime? entryEarliest, DateTime? entryLatest) = await auditReader.GetDateRangeAsync();
            (DateTime? changeEarliest, DateTime? changeLatest) = await changeReader.GetDateRangeAsync();

            return Results.Ok(new AuditDateRangeResponse
            {
                EarliestDate = Earlier(entryEarliest, changeEarliest),
                LatestDate   = Later(entryLatest, changeLatest),
            });
        })
        .WithName("GetAuditDateRange")
        .WithSummary("Get the audit trail's available date range")
        .Produces<AuditDateRangeResponse>(StatusCodes.Status200OK)
        .WithDescription(
            "Returns the earliest and latest timestamp across both `Audit_Entry` and `Audit_Change` " +
            "combined — so a caller knows what range actually has data before requesting " +
            "`GET /api/v1/admin/audit/export`. Both fields are `null` when neither table has any rows.");

        publicGroup.MapGet("/audit/export", async (
            string? startDate,
            string? endDate,
            IAuditEntryReader auditReader,
            IChangeReader changeReader,
            IApiLocalizer localizer,
            IConfiguration configuration,
            HttpContext httpContext) =>
        {
            if (!TryParseUtcDate(startDate, out DateTime? start))
                return Results.Problem(detail: localizer[ApiMessages.AuditExportDateInvalid], statusCode: StatusCodes.Status422UnprocessableEntity);
            if (!TryParseUtcDate(endDate, out DateTime? end))
                return Results.Problem(detail: localizer[ApiMessages.AuditExportDateInvalid], statusCode: StatusCodes.Status422UnprocessableEntity);
            if (start is not null && end is not null && start > end)
                return Results.Problem(detail: localizer[ApiMessages.AuditExportDateRangeInvalid], statusCode: StatusCodes.Status422UnprocessableEntity);

            int entryCount  = await auditReader.CountInRangeAsync(start, end);
            int changeCount = await changeReader.CountInRangeAsync(start, end);
            int totalCount  = entryCount + changeCount;
            int maxRows     = configuration.GetValue<int?>("Quotinator:AdminAuditExportMaxRows") ?? QueryParamDefaults.AdminAuditExportMaxRows;

            if (totalCount > maxRows)
                return Results.Problem(
                    detail: localizer.Format(ApiMessages.AuditExportRowCapExceeded, totalCount, maxRows),
                    statusCode: StatusCodes.Status422UnprocessableEntity);

            IReadOnlyList<AuditEntryEntity> entries = await auditReader.GetAllInRangeAsync(start, end);
            IReadOnlyList<ChangeEntity> changes = await changeReader.GetAllInRangeAsync(start, end);

            httpContext.Response.Headers.Append("Content-Disposition",
                $"attachment; filename=\"quotinator-audit-export-{DateTime.UtcNow:yyyyMMddHHmmss}.json\"");

            return Results.Ok(new AuditExportResponse
            {
                Entries = [.. entries.Select(ToAuditEntryResponse)],
                Changes = [.. changes.Select(ToAuditChangeResponse)],
            });
        })
        .WithName("ExportAuditTrail")
        .WithSummary("Bulk-export the audit trail")
        .Produces<AuditExportResponse>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)
        .WithDescription(
            "Returns every `Audit_Entry` and `Audit_Change` row within an optional date range, in one " +
            "call — a downloaded JSON file (`Content-Disposition: attachment`), not a paginated response; " +
            "the caller already decided it wants the full set. `startDate`/`endDate` are optional and " +
            "unbounded on whichever side is omitted; use `GET /api/v1/admin/audit/date-range` first to " +
            "learn the range that actually has data. Returns `422` if either date fails to parse, if " +
            "`startDate` is after `endDate`, or if the combined row count would exceed " +
            "`Quotinator:AdminAuditExportMaxRows` (default 50,000) — narrow the range and retry rather " +
            "than receiving a silently truncated file. No `X-Api-Key` required, matching `GET /admin/audit`'s precedent.");

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

        adminGroup.MapPost("/database/reset", async (IDatabaseInitializer db, Quotinator.Api.Startup.DatabaseHealthState dbHealth, INotificationWriter notificationWriter, IAppVersionTracker appVersionTracker, IVersionService versionService, IAuditEntryWriter auditWriter, ICallerContext callerContext, ILogger<Program> logger, bool preserveSchemaVersion = false, bool forceSourceRefresh = false, bool allowNoBackup = false) =>
        {
            DatabaseOperationResult reset = await db.ResetAsync(preserveSchemaVersion, forceSourceRefresh, allowNoBackup);

            // #348: a reset that could not take a backup did not run. 200 means the endpoint did what
            // was asked; this did not, so it is an error whose body carries the cause and what the
            // operator can do about it. 409 rather than 500: nothing failed unexpectedly — the state of
            // the backup storage conflicts with running a destructive operation, and that is a
            // condition the caller can resolve and retry.
            if (!reset.Succeeded)
            {
                BackupOutcome obstacle = reset.BackupObstacle ?? BackupOutcome.Unclassified;
                return Results.Problem(
                    title: "Reset refused — no backup could be taken",
                    detail: BackupObstacleGuidance.Cause(obstacle),
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?>
                    {
                        ["backupObstacle"] = obstacle.ToString(),
                        ["remedies"]       = BackupObstacleGuidance.Remedies(obstacle),
                    });
            }

            // #348: a backup that was skipped by explicit override is recorded where it will still be
            // found long after the log has rotated. Without this, "there is no backup from that date"
            // has no answer but guesswork — which is exactly what the override must not cost.
            if (reset.BackupSkippedByOverride)
            {
                await auditWriter.WriteAsync(new AuditEntryEntity
                {
                    TableName   = "Database",
                    Operation   = AuditOperation.BackupSkipped,
                    Agent       = callerContext.Agent,
                    PerformedAt = DateTime.UtcNow,
                });
            }

            dbHealth.MarkHealthy();
            // #278: dismiss any ActionRequired notification recommending a Reset, now that one has
            // actually completed. Reset itself drops and rebuilds System_Notification along with
            // every other table (no protected/excluded set — see CLAUDE.md's "No exception-based
            // migration recovery"), so in practice this call always affects zero rows immediately
            // after ResetAsync — the table is already empty. Kept anyway, matching #278's own
            // explicit wiring instruction: it's the correct call site for the general mechanism
            // (a future action that does *not* wipe the whole database would make it load-bearing),
            // and it's harmless here.
            await notificationWriter.DismissByTriggerAsync(NotificationDismissTrigger.DatabaseReset);
            // #81: Reset rebuilds System_AppVersion empty like every other table (no protected set) —
            // re-populate it immediately so it stays "always provided with content" rather than only
            // getting a row again on the next full app restart. The version hasn't actually changed
            // (Reset wipes data, not the running build), so this is a same-version overwrite in the
            // common case — harmless, and correct if a Reset ever coincides with a version change.
            // Non-fatal, matching Program.cs's own startup treatment of this same call — a test's
            // stubbed IDatabaseInitializer (e.g. NoOpDatabaseInitializer) never actually creates
            // System_AppVersion, and this must never turn a successful Reset into a failed response.
            try
            {
                await appVersionTracker.RecordCurrentAsync(versionService.Application, versionService.Version);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Server] Failed to record the current app version after Reset — non-fatal, the reset itself still succeeded.");
            }
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
        .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
        .WithDescription(
            "Drops the entire database and rebuilds it from scratch via the baseline schema — equivalent to " +
            "deleting the database file and restarting, except it does **not** reimport any bundled or user " +
            "quote content afterward (issue #156). Every table is dropped, including the audit log — no table " +
            "is protected from this reset; export the audit trail first via `GET /api/v1/admin/audit/export` " +
            "if you need to keep it (issue #249). " +
            "By default, schema migration history is also cleared and rebuilt to the latest version; pass `preserveSchemaVersion=true` to keep the existing migration history's per-version rows instead. " +
            "Auto-updated sources are still refreshed from the network first if stale (or unconditionally when `forceSourceRefresh=true`), " +
            "unless `Quotinator:AutoUpdateSources` is `false` — this only refreshes the on-disk source cache, " +
            "independent of the database, since nothing gets imported by this call. " +
            "Returns the row counts (all zero immediately after a reset) and a per-file, per-entity-type report (issue #221); " +
            "the report reflects no activity since Reset does not seed. " +
            "A reset takes a safety backup first, and **refuses with `409 Conflict` if one cannot be taken** (issue #348) — " +
            "the response names which obstacle stopped it (`backupObstacle`) and what can be done about it (`remedies`). " +
            "Pass `allowNoBackup=true` to proceed anyway: this both accepts responsibility for there being no restore point " +
            "and asserts the reset can complete without one. It also unlocks the reserve above the normal backup quota, so a " +
            "reset blocked only by that quota takes a real backup rather than none at all. A backup skipped this way is " +
            "recorded in the audit trail, so it stays discoverable long after the log has rotated. " +
            "Protected by a concurrency-1 limiter — a second call while one is in progress receives `429 Too Many Requests` immediately. " +
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`. Returns `401` if the key is not configured or does not match.");

        adminGroup.MapPost("/sources/refresh", async (IDatabaseInitializer db, bool force = false) =>
        {
            SourceCacheResolution resolution = await db.RefreshSourcesAsync(force);
            return Results.Ok(new SourceRefreshResponse
            {
                Results = [.. resolution.Results.Select(r => new SourceRefreshResultResponse
                {
                    Name               = r.Name,
                    Url                = r.Url,
                    Outcome            = r.Outcome.ToString().ToLowerInvariant(),
                    Detail             = r.Detail,
                    LastRefreshedAtUtc = r.LastRefreshedAtUtc
                })]
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
            "An unscoped clear (`table` omitted) also clears the change log (`Audit_Change`) — #249 treats " +
            "both as one combined audit-trail concern, matching `GET .../audit/export`/`.../date-range`. " +
            "A scoped clear leaves `Audit_Change` untouched, since it has no equivalent per-table scoping. " +
            "A single audit entry recording the purge is written after the delete so there is always a trace that a clear occurred. " +
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`. Returns `401` if the key is not configured or does not match.");
    }

    // Parses an optional startDate/endDate query value as UTC — DateTimeStyles.AssumeUniversal treats
    // an offset-less value (e.g. "2026-01-01") as already UTC rather than local server time, matching
    // how PerformedAt/OccurredAt are always stored; AdjustToUniversal converts an explicit-offset value
    // (e.g. with "Z" or "+02:00") to UTC instead of rejecting it.
    private static bool TryParseUtcDate(string? value, out DateTime? result)
    {
        if (value is null) { result = null; return true; }

        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime parsed))
        {
            result = null;
            return false;
        }

        result = parsed;
        return true;
    }

    private static DateTime? Earlier(DateTime? a, DateTime? b) =>
        a is null ? b : b is null ? a : a < b ? a : b;

    private static DateTime? Later(DateTime? a, DateTime? b) =>
        a is null ? b : b is null ? a : a > b ? a : b;

    private static AuditEntryResponse ToAuditEntryResponse(AuditEntryEntity entity) => new()
    {
        Id           = entity.Id.ToCanonicalId(),
        TableName    = entity.TableName,
        RecordId     = entity.RecordId,
        Operation    = entity.Operation,
        Agent        = entity.Agent,
        PerformedAt  = entity.PerformedAt,
        DateCreated  = entity.DateCreated.Parsed,
        DateModified = entity.DateModified.Parsed,
        DateDeleted  = entity.DateDeleted.Parsed,
        IsDeleted    = entity.IsDeleted,
    };

    private static AuditChangeResponse ToAuditChangeResponse(ChangeEntity entity) => new()
    {
        Id              = entity.Id.ToCanonicalId(),
        EntityType      = entity.EntityType,
        EntityId        = entity.EntityId,
        InitiatedByType = entity.InitiatedByType.Parsed,
        InitiatedById   = entity.InitiatedById,
        Action          = entity.Action.Parsed,
        Field           = entity.Field,
        OldValue        = entity.OldValue,
        NewValue        = entity.NewValue,
        OccurredAt      = entity.OccurredAt,
        DateCreated     = entity.DateCreated.Parsed,
        DateModified    = entity.DateModified.Parsed,
        DateDeleted     = entity.DateDeleted.Parsed,
        IsDeleted       = entity.IsDeleted,
    };
}

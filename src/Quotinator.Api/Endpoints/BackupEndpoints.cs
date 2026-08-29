using System.ComponentModel;
using Microsoft.AspNetCore.Mvc;
using Quotinator.Api.Endpoints.Filters;
using Quotinator.Api.Endpoints.Shared;
using Quotinator.Api.Startup;
using Quotinator.Constants.Api;
using Quotinator.Constants.RateLimiting;
using Quotinator.Core.Models;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Api.Endpoints;

/// <summary>
/// Registers the <c>/api/v1/admin/backups</c> endpoints (#349).
/// <para>
/// Their own group, and their own OpenAPI category. The routes still sit under <c>/api/v1/admin</c>
/// and still chain the admin API key and the concurrency-1 limiter — the separation is a
/// documentation grouping, not a relaxation of who may call them.
/// </para>
/// <para>
/// Nothing here opens the database. That is what lets every route answer while the database is
/// degraded, which is the state an operator is in when they most need to see, take, or free up a
/// backup.
/// </para>
/// </summary>
internal static class BackupEndpoints
{
    private const string GetAllBackupsName    = "GetAllBackups";
    private const string DeleteBackupName     = "DeleteBackup";
    private const string GetBackupContentName = "GetBackupContent";
    private const string CreateBackupName     = "CreateBackup";
    private const string GetBackupStatusName  = "GetBackupStatus";

    internal static void MapBackupEndpoints(this WebApplication app)
    {
        RouteGroupBuilder backups = app.MapGroup("/api/v1/admin/backups")
                                       .WithTags(ApiTags.Backup)
                                       .RequireRateLimiting(RateLimitPolicies.Admin)
                                       .AddEndpointFilter<AdminApiKeyFilter>()
                                       .WithMetadata(AdminApiKeyRequiredMarker.Instance);

        backups.MapGet("/", (
            IDatabaseBackupReader reader,
            IApiLocalizer localizer,
            [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
            [Description("Number of backups per page (0–500). 0 means every backup as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null) =>
        {
            if (!PaginationParsing.TryParse(page, pageSize, localizer, out int pageValue, out int pageSizeValue, out IResult? pageError))
                return pageError!;

            IReadOnlyList<BackupFileInfo> all = reader.List();

            // pageSize = 0 is "every row as one page", and the response reports the count actually
            // returned rather than the literal 0 asked for — #183's effective-size contract.
            int effectiveSize = pageSizeValue == 0 ? Math.Max(all.Count, 1) : pageSizeValue;
            BackupResponse[] items =
            [
                .. all.Skip((pageValue - 1) * effectiveSize)
                      .Take(effectiveSize)
                      .Select(b => new BackupResponse { Name = b.Name, SizeBytes = b.SizeBytes, TakenAtUtc = b.TakenAtUtc })
            ];

            PagedItems<BackupResponse> result = new(items, pageValue, pageSizeValue == 0 ? all.Count : pageSizeValue, all.Count);

            return PaginationParsing.ValidatePageBeyondLast(pageValue, result.TotalPages, localizer)
                ?? Results.Ok(result);
        })
        .WithName(GetAllBackupsName)
        .WithSummary("List backups")
        .Produces<PagedItems<BackupResponse>>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
        .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)
        .WithDescription(
            "Returns a paginated list of the database backups currently stored in `{dataDir}/backups/`, newest first, " +
            "each with the facts needed to decide which to remove: file name, size in bytes, and when it was taken. " +
            "An empty backups folder returns an empty page, not a `404`. " +
            "`pageSize=0` returns every backup as a single page. " +
            "Reads no database content, so it answers while the database is degraded. " +
            "Protected by a concurrency-1 limiter — a second call while one is in progress receives `429 Too Many Requests` immediately. " +
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`.");

        backups.MapGet("/status", (
            IDatabaseInitializer db,
            IDatabaseBackupReader reader) =>
        {
            BackupOutcome readiness = db.CheckBackupReadiness();
            bool           canBackUp = readiness == BackupOutcome.Succeeded;
            BackupStorageUsage usage = reader.GetUsage();

            return Results.Ok(new BackupStatusResponse
            {
                CanBackUp = canBackUp,
                Obstacle  = canBackUp ? null : readiness.ToString(),
                Cause     = canBackUp ? null : BackupObstacleGuidance.Cause(readiness),
                Remedies  = canBackUp ? [] : BackupObstacleGuidance.Remedies(readiness),
                Storage   = new BackupStorageResponse
                {
                    UsedBytes                    = usage.UsedBytes,
                    FileCount                    = usage.FileCount,
                    QuotaBytes                   = usage.QuotaBytes,
                    CeilingBytes                 = usage.CeilingBytes,
                    QuotaPercent                 = usage.QuotaPercent,
                    RemainingAgainstQuotaBytes   = usage.RemainingAgainstQuotaBytes,
                    RemainingAgainstCeilingBytes = usage.RemainingAgainstCeilingBytes,
                    UsedPercentOfCeiling         = usage.UsedPercentOfCeiling,
                    ReserveInUse                 = usage.ReserveInUse,
                    FreeDiskBytes                = usage.FreeDiskBytes,
                },
            });
        })
        // Deviates from the three standard endpoint-name shapes deliberately, as the convention allows
        // when a genuinely different operation shape would be misdescribed by them: this is not a list,
        // not a fetch by id, and not an action that changes anything.
        .WithName(GetBackupStatusName)
        .WithSummary("Backup status")
        .Produces<BackupStatusResponse>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
        .WithDescription(
            "Answers the question an operator actually has before acting: **can a backup be made right now, and are we within quota?** " +
            "`canBackUp` is the same pre-flight check a reset makes; when it is `false`, `obstacle`, `cause` and `remedies` name which of the " +
            "five obstacles is in the way and what can be done about it. " +
            "`storage` reports used total, the operating quota (default 90% of the budget), the absolute ceiling, what remains against each, " +
            "the percentage used, and whether the reserve between quota and ceiling is currently being relied on — alongside **real free disk space**, " +
            "which is an independent, physical constraint rather than part of the quota. " +
            "Reads no database content and touches the filesystem only to the extent of the pre-flight's own zero-byte writability probe, " +
            "so it answers while the database is degraded — which is the state it exists for. " +
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`.");

        backups.MapPost("/create", async (
            IDatabaseInitializer db,
            IAuditEntryWriter auditWriter,
            ICallerContext callerContext) =>
        {
            DatabaseBackupResult result = await db.CreateBackupAsync();

            if (!result.Succeeded)
            {
                // 409 rather than 500, for the same reason a refused reset returns one: nothing failed
                // unexpectedly — the state of backup storage conflicts with taking a backup, and that
                // is a condition the caller can resolve and retry.
                return Results.Problem(
                    title: "Backup refused — no backup could be taken",
                    detail: BackupObstacleGuidance.Cause(result.Outcome),
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?>
                    {
                        ["backupObstacle"] = result.Outcome.ToString(),
                        ["remedies"]       = BackupObstacleGuidance.Remedies(result.Outcome),
                    });
            }

            string name = Path.GetFileName(result.Path!);

            await auditWriter.WriteAsync(new AuditEntryEntity
            {
                TableName   = "Database",
                RecordId    = name,
                Operation   = AuditOperation.Backup,
                Agent       = callerContext.Agent,
                PerformedAt = DateTime.UtcNow,
            });

            return Results.Created($"/api/v1/admin/backups/{name}", new BackupResponse
            {
                Name       = name,
                SizeBytes  = File.Exists(result.Path) ? new FileInfo(result.Path!).Length : 0L,
                TakenAtUtc = DateTime.UtcNow,
            });
        })
        .WithName(CreateBackupName)
        .WithSummary("Create a backup")
        .Produces<BackupResponse>(StatusCodes.Status201Created)
        .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
        .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
        .WithDescription(
            "Takes a backup of the database now and returns the file it wrote, so it can then be downloaded. " +
            "Every other backup this application takes is a side effect of a migration, a seed or a reset; this is the one an operator " +
            "invokes deliberately — for instance before doing something they may want to undo. " +
            "**Refuses with `409 Conflict` when a backup cannot be taken**, naming which obstacle stopped it (`backupObstacle`) and what can be " +
            "done about it (`remedies`), rather than reporting a success that produced no file. " +
            "The refusal matches what `GET /api/v1/admin/backups/status` reports, since both consult the same pre-flight check. " +
            "A successful backup is recorded in the audit trail. " +
            "Protected by a concurrency-1 limiter — a second call while one is in progress receives `429 Too Many Requests` immediately. " +
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`.");

        backups.MapGet("/{name}/content", (
            string name,
            IDatabaseBackupReader reader,
            IApiLocalizer localizer) =>
        {
            if (!reader.IsValidName(name))
                return Results.Problem(detail: localizer[ApiMessages.BackupNameInvalid], statusCode: StatusCodes.Status422UnprocessableEntity);

            Stream? content = reader.OpenRead(name);
            return content is null
                ? Results.Problem(detail: localizer[ApiMessages.BackupNotFound], statusCode: StatusCodes.Status404NotFound)
                : Results.File(content, "application/octet-stream", Path.GetFileName(name));
        })
        // A fetch, but by name rather than by id — noted here as the convention requires, rather than
        // forced into "X by ID" wording that would describe a Guid lookup this is not.
        .WithName(GetBackupContentName)
        .WithSummary("Backup content by name")
        .Produces<FileResult>(StatusCodes.Status200OK, "application/octet-stream")
        .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
        .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
        .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)
        .WithDescription(
            "Downloads one stored backup, so a restore point can survive the container it was taken in. " +
            "`name` is the file name as `GET /api/v1/admin/backups` reports it — a name, never a path: anything containing a path separator or a " +
            "traversal segment is rejected with `422` before the filesystem is touched. An unknown name is a `404`, never an empty file. " +
            "The response is streamed with `Content-Disposition: attachment` naming the stored file. " +
            "Note that the admin limiter permits one operation at a time, so a large download holds that permit until it completes. " +
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`.");

        backups.MapDelete("/{name}", async (
            string name,
            IDatabaseBackupReader reader,
            IDatabaseBackupWriter writer,
            IAuditEntryWriter auditWriter,
            ICallerContext callerContext,
            IApiLocalizer localizer) =>
        {
            if (!reader.IsValidName(name))
                return Results.Problem(detail: localizer[ApiMessages.BackupNameInvalid], statusCode: StatusCodes.Status422UnprocessableEntity);

            if (!writer.Delete(name))
                return Results.Problem(detail: localizer[ApiMessages.BackupNotFound], statusCode: StatusCodes.Status404NotFound);

            // Removing a backup removes a restore point, so it is recorded where it will still be found
            // long after the log has rotated — the reason an endpoint is preferred over deleting the
            // file by hand in the first place.
            await auditWriter.WriteAsync(new AuditEntryEntity
            {
                TableName   = "Database",
                RecordId    = name,
                Operation   = AuditOperation.BackupDeleted,
                Agent       = callerContext.Agent,
                PerformedAt = DateTime.UtcNow,
            });

            return Results.NoContent();
        })
        .WithName(DeleteBackupName)
        // Summary reads Remove rather than the DML keyword this endpoint's own verb shares its name
        // with: SqlSourceScanTests flags any quoted text beginning with one, and that keyword followed
        // by a space would trip it. The guard is a CVE gate and is not worth loosening for a summary
        // line; the imperative-verb-first convention is satisfied either way, and WithName still says
        // DeleteBackup, which is what a generated client actually depends on.
        .WithSummary("Remove a backup")
        .Produces(StatusCodes.Status204NoContent)
        .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
        .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
        .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)
        .WithDescription(
            "Removes one stored backup, freeing quota so that backups become possible again once the folder is full. " +
            "`name` is the file name as `GET /api/v1/admin/backups` reports it — a name, never a path: anything containing a path separator or a " +
            "traversal segment is rejected with `422` before the filesystem is touched, and nothing is removed. " +
            "Deleting a name that does not exist is a `404`, so a caller can tell \"removed\" from \"was never there\". " +
            "Every successful deletion is recorded in the audit trail, naming the file. " +
            "Protected by a concurrency-1 limiter — a second call while one is in progress receives `429 Too Many Requests` immediately. " +
            "Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`.");
    }
}

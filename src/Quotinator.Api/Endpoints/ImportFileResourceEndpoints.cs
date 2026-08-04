using System.ComponentModel;
using Microsoft.AspNetCore.Mvc;
using Quotinator.Api.Endpoints.Filters;
using Quotinator.Api.Endpoints.Shared;
using Quotinator.Constants.Api;
using Quotinator.Constants.RateLimiting;
using Quotinator.Core.Models;
using Quotinator.Core.Services;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Helpers;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Api.Endpoints;

/// <summary>
/// Registers <c>/api/v1/import/file-resources</c> — captured import/seed file content provenance
/// (#251). Lives alongside the rest of the import surface (matching <see cref="ImportRuleEndpoints"/>'s
/// own precedent) rather than under <c>/api/v1/admin</c> — a captured file's content is import
/// infrastructure, not database administration.
/// </summary>
internal static class ImportFileResourceEndpoints
{
    internal static void MapImportFileResourceEndpoints(this WebApplication app)
    {
        // Non-destructive endpoints — read-only; no API key required.
        var publicGroup = app.MapGroup("/api/v1/import/file-resources")
                             .WithTags(ApiTags.Import)
                             .RequireRateLimiting(RateLimitPolicies.Admin);

        // Destructive endpoints — require X-Api-Key header.
        var adminGroup = app.MapGroup("/api/v1/import/file-resources")
                            .WithTags(ApiTags.Import)
                            .RequireRateLimiting(RateLimitPolicies.Admin)
                            .AddEndpointFilter<AdminApiKeyFilter>()
                            .WithMetadata(AdminApiKeyRequiredMarker.Instance);

        publicGroup.MapGet("/", async (
            string? fileName,
            string? origin,
            IFileResourceRepository fileResources,
            IApiLocalizer localizer,
            [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
            [Description("Number of entries per page (0-500). 0 means every matching file resource as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null) =>
        {
            if (!PaginationParsing.TryParse(page, pageSize, localizer, out var pageValue, out var pageSizeValue, out var pageError))
                return pageError!;

            FileResourceOrigin? parsedOrigin = null;
            if (origin is not null)
            {
                if (!Enum.TryParse<FileResourceOrigin>(origin, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                    return Results.Problem(detail: localizer[ApiMessages.FileResourceOriginInvalid], statusCode: StatusCodes.Status422UnprocessableEntity);
                parsedOrigin = parsed;
            }

            var result = await fileResources.GetPageAsync(fileName, parsedOrigin, pageValue, pageSizeValue);

            var beyondLastError = PaginationParsing.ValidatePageBeyondLast(pageValue, result.TotalPages, localizer);
            if (beyondLastError is not null) return beyondLastError;

            var mapped = new PagedItems<FileResourceResponse>(
                result.Items.Select(ToResponse).ToList(), result.Page, result.PageSize, result.TotalCount);
            return Results.Ok(mapped);
        })
        .WithName("GetFileResources")
        .WithSummary("List captured import files")
        .Produces<PagedItems<FileResourceResponse>>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)
        .WithDescription(
            "Returns a paginated list of captured import/seed file content (#251), each distinct " +
            "`fileName` ordered by most-recently-seen first. Filter by `fileName` (exact match, " +
            "case-insensitive) and/or `origin` (`system`, `user`, `upload`). Each row includes " +
            "`linkedBatchCount` — the number of `Import_Batch` rows this content is linked to — but not " +
            "the individual batch ids (see `GET .../{id}` for those) or line content (see " +
            "`GET .../{id}/download`). Maximum `pageSize` is 500.");

        publicGroup.MapGet("/{id}", async (
            string id,
            IFileResourceRepository fileResources,
            IApiLocalizer localizer) =>
        {
            if (!Guid.TryParse(id, out var fileResourceId))
                return Results.Problem(detail: localizer[ApiMessages.FileResourceNotFound], statusCode: StatusCodes.Status404NotFound);

            var fileResource = await fileResources.FindAsync(fileResourceId);
            if (fileResource is null)
                return Results.Problem(detail: localizer[ApiMessages.FileResourceNotFound], statusCode: StatusCodes.Status404NotFound);

            var batchIds = await fileResources.GetBatchIdsAsync(fileResourceId);
            return Results.Ok(ToResponse(fileResource, batchIds.Select(b => b.ToCanonicalId()).ToList()));
        })
        .WithName("GetFileResourceById")
        .WithSummary("Captured import file by id")
        .Produces<FileResourceResponse>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
        .WithDescription(
            "Returns a single captured file resource's metadata (#251), including the full " +
            "`linkedBatchIds` list (unlike the list endpoint, which only reports `linkedBatchCount`). " +
            "Returns `404` for an unknown or malformed id. No line content — see `GET .../{id}/download`.");

        publicGroup.MapGet("/{id}/download", async (
            string id,
            string? lineEnding,
            IFileResourceRepository fileResources,
            IApiLocalizer localizer) =>
        {
            if (!Guid.TryParse(id, out var fileResourceId))
                return Results.Problem(detail: localizer[ApiMessages.FileResourceNotFound], statusCode: StatusCodes.Status404NotFound);

            var fileResource = await fileResources.FindAsync(fileResourceId);
            if (fileResource is null)
                return Results.Problem(detail: localizer[ApiMessages.FileResourceNotFound], statusCode: StatusCodes.Status404NotFound);

            var effectiveLineEnding = fileResource.LineEnding.Parsed!.Value;
            if (lineEnding is not null)
            {
                if (!Enum.TryParse<LineEndingStyle>(lineEnding, ignoreCase: true, out var overrideLineEnding) || !Enum.IsDefined(overrideLineEnding))
                    return Results.Problem(detail: localizer[ApiMessages.LineEndingInvalid], statusCode: StatusCodes.Status422UnprocessableEntity);
                effectiveLineEnding = overrideLineEnding;
            }

            var lines = await fileResources.GetLinesAsync(fileResourceId);
            var content = FileContentSplitter.Join(
                lines.Select(l => l.Text).ToList(), effectiveLineEnding, fileResource.EndsWithTrailingNewline);

            return Results.Text(content, "text/plain");
        })
        .WithName("DownloadFileResource")
        .WithSummary("Reconstruct a captured import file's original content")
        .Produces<string>(StatusCodes.Status200OK, "text/plain")
        .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
        .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)
        .WithDescription(
            "Reassembles the `Import_FileResourceLine` rows captured for `id` (#251) back into the file's " +
            "original text. Defaults to the line-ending style and trailing-newline presence recorded when " +
            "the file was first captured; pass `lineEnding` (`lf`, `crlf`, or `cr`, case-insensitive) to " +
            "normalize the output to a different style instead — trailing-newline presence is never " +
            "overridden. Returns `404` for an unknown or soft-deleted `id`. No `X-Api-Key` required — " +
            "read-only, matching `GET /admin/audit`'s precedent.");

        adminGroup.MapPost("/prune", async (
            IFileResourceRepository fileResources,
            IApiLocalizer localizer,
            [Description("Number of most-recently-seen rows to keep per distinct FileName (>= 0)."), DefaultValue(QueryParamDefaults.KeepPerFile)] string? keepPerFile = null) =>
        {
            var keepPerFileValue = QueryParamDefaults.KeepPerFile;
            if (keepPerFile is not null && (!int.TryParse(keepPerFile, out keepPerFileValue) || keepPerFileValue < 0))
                return Results.Problem(detail: localizer[ApiMessages.KeepPerFileInvalid], statusCode: StatusCodes.Status422UnprocessableEntity);

            var prunedCount = await fileResources.PruneAsync(keepPerFileValue);
            return Results.Ok(new FileResourcePruneResponse { PrunedCount = prunedCount });
        })
        .WithName("PruneFileResources")
        .WithSummary("Prune old captured import file content")
        .Produces<FileResourcePruneResponse>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
        .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)
        .WithDescription(
            "Hard-deletes every `Import_FileResource` row beyond the `keepPerFile` most-recently-seen " +
            "(by `LastSeenAtUtc`) distinct rows per `FileName` (#251), cascading to the matching " +
            "`Import_FileResourceLine`/`Import_FileResourceBatch` rows — the originating `Import_Batch` " +
            "rows themselves are never touched, only the file content copy. Returns the number of rows " +
            "pruned. Requires `X-Api-Key: <key>` matching `Quotinator:AdminApiKey`.");
    }

    private static FileResourceResponse ToResponse(FileResourceListItem item) => new()
    {
        Id                      = item.Id.ToCanonicalId(),
        FileName                = item.FileName,
        OriginalFolderPath      = item.OriginalFolderPath,
        Origin                  = item.Origin.Parsed?.ToString().ToLowerInvariant() ?? item.Origin.Raw,
        HomeDirectoryKey        = item.HomeDirectoryKey,
        ContentHash             = item.ContentHash,
        LineEnding              = item.LineEnding.Parsed?.ToString().ToLowerInvariant() ?? item.LineEnding.Raw,
        EndsWithTrailingNewline = item.EndsWithTrailingNewline,
        Converter               = item.Converter,
        ConverterOptions        = item.ConverterOptions,
        FirstSeenAtUtc          = item.FirstSeenAtUtc.Parsed,
        LastSeenAtUtc           = item.LastSeenAtUtc.Parsed,
        LinkedBatchCount        = item.LinkedBatchCount,
        LinkedBatchIds          = null,
    };

    private static FileResourceResponse ToResponse(FileResourceEntity entity, IReadOnlyList<string> linkedBatchIds) => new()
    {
        Id                      = entity.Id.ToCanonicalId(),
        FileName                = entity.FileName,
        OriginalFolderPath      = entity.OriginalFolderPath,
        Origin                  = entity.Origin.Parsed?.ToString().ToLowerInvariant() ?? entity.Origin.Raw,
        HomeDirectoryKey        = entity.HomeDirectoryKey,
        ContentHash             = entity.ContentHash,
        LineEnding              = entity.LineEnding.Parsed?.ToString().ToLowerInvariant() ?? entity.LineEnding.Raw,
        EndsWithTrailingNewline = entity.EndsWithTrailingNewline,
        Converter               = entity.Converter,
        ConverterOptions        = entity.ConverterOptions,
        FirstSeenAtUtc          = entity.FirstSeenAtUtc.Parsed,
        LastSeenAtUtc           = entity.LastSeenAtUtc.Parsed,
        LinkedBatchCount        = linkedBatchIds.Count,
        LinkedBatchIds          = linkedBatchIds,
    };
}

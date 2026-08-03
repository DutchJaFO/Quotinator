using System.ComponentModel;
using Microsoft.AspNetCore.Mvc;
using Quotinator.Api.Endpoints.Shared;
using Quotinator.Constants.Api;
using Quotinator.Constants.RateLimiting;
using Quotinator.Core.Models;
using Quotinator.Core.Services;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Helpers;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Api.Endpoints;

/// <summary>
/// Registers <c>/api/v1/import/batches</c> — browsing the <c>Import_Batch</c> history (#251). Wraps
/// <see cref="IImportBatchRepository.GetPagedAsync"/> and the base repository's own <c>GetByIdAsync</c>,
/// which already existed for internal use but had no HTTP surface.
/// </summary>
internal static class ImportBatchEndpoints
{
    internal static void MapImportBatchEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/import/batches")
                       .WithTags(ApiTags.Import)
                       .RequireRateLimiting(RateLimitPolicies.Admin);

        group.MapGet("/", async (
            string? type,
            string? status,
            IImportBatchRepository batches,
            IApiLocalizer localizer,
            [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
            [Description("Number of entries per page (0-500). 0 means every matching batch as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null) =>
        {
            if (!PaginationParsing.TryParse(page, pageSize, localizer, out var pageValue, out var pageSizeValue, out var pageError))
                return pageError!;

            ImportBatchType? parsedType = null;
            if (type is not null)
            {
                if (!Enum.TryParse<ImportBatchType>(type, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                    return Results.Problem(detail: localizer[ApiMessages.ImportBatchTypeInvalid], statusCode: StatusCodes.Status422UnprocessableEntity);
                parsedType = parsed;
            }

            ImportBatchStatus? parsedStatus = null;
            if (status is not null)
            {
                if (!Enum.TryParse<ImportBatchStatus>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                    return Results.Problem(detail: localizer[ApiMessages.ImportBatchStatusInvalid], statusCode: StatusCodes.Status422UnprocessableEntity);
                parsedStatus = parsed;
            }

            var result = await batches.GetPagedAsync(parsedType, parsedStatus, pageValue, pageSizeValue);

            var beyondLastError = PaginationParsing.ValidatePageBeyondLast(pageValue, result.TotalPages, localizer);
            if (beyondLastError is not null) return beyondLastError;

            var mapped = new PagedItems<ImportBatchResponse>(
                result.Items.Select(ToResponse).ToList(), result.Page, result.PageSize, result.TotalCount);
            return Results.Ok(mapped);
        })
        .WithName("GetImportBatches")
        .WithSummary("List import batches")
        .Produces<PagedItems<ImportBatchResponse>>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)
        .WithDescription(
            "Returns a paginated list of import batches (#251) — every seed/reseed file and every " +
            "`POST /import` call, newest first. Filter by `type` (`seed`, `userseed`, `import`, " +
            "`system`) and/or `status` (`staged`, `applied`, `discarded`). Maximum `pageSize` is 500.");

        group.MapGet("/{id}", async (
            string id,
            IImportBatchRepository batches,
            IApiLocalizer localizer) =>
        {
            ImportBatchEntity? entity = Guid.TryParse(id, out var batchId) ? await batches.GetByIdAsync(batchId) : null;
            var response = entity is null ? null : ToResponse(entity);
            return Shared.NotFoundResult.OkOrNotFound(response, localizer, ApiMessages.ImportBatchNotFound);
        })
        .WithName("GetImportBatchById")
        .WithSummary("Import batch by id")
        .Produces<ImportBatchResponse>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
        .WithDescription("Returns a single import batch by id. Matches case-insensitively. Returns `404` if not found.");
    }

    private static ImportBatchResponse ToResponse(ImportBatchEntity entity) => new()
    {
        Id             = entity.Id.ToCanonicalId(),
        Name           = entity.Name,
        Type           = entity.Type.Parsed?.ToString().ToLowerInvariant() ?? entity.Type.Raw,
        Url            = entity.Url,
        ImportedAt     = entity.ImportedAt,
        ImportedById   = entity.ImportedById,
        RecordCount    = entity.RecordCount,
        ConflictPolicy = entity.ConflictPolicy.Parsed?.ToString().ToLowerInvariant() ?? entity.ConflictPolicy.Raw,
        Status         = entity.Status.Parsed?.ToString().ToLowerInvariant() ?? entity.Status.Raw,
        AppliedAt      = entity.AppliedAt,
    };
}

using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Quotinator.Api.Endpoints.Shared;
using Quotinator.Api.Models;
using Quotinator.Constants.Api;
using Quotinator.Constants.RateLimiting;
using Quotinator.Core.Services;
using Quotinator.Data.Entities;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Engine.Entities;

namespace Quotinator.Api.Endpoints;

/// <summary>Registers all <c>/api/v1/masterdata/stagedirections</c> endpoints.</summary>
internal static class StageDirectionEndpoints
{
    // Static classes cannot be type arguments (CS0718); this nested class is the ILogger<T> category.
    private sealed class Log { }

    internal static void MapStageDirectionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/masterdata/stagedirections")
                       .WithTags(ApiTags.MasterData)
                       .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapGet("/", GetAll)
             .WithName("GetAllStageDirections")
             .WithSummary("List stage directions")
             .WithDescription(
                 "Returns a paginated list of stage directions. Maximum `pageSize` is 500. " +
                 "`pageSize=0` returns every stage direction as a single page.");

        group.MapGet("/{id}", GetById)
             .WithName("GetStageDirectionById")
             .WithSummary("Stage direction by ID")
             .WithDescription("Returns a single stage direction by ID. Matches case-insensitively. Returns 404 if not found.");
    }

    private static async Task<IResult> GetAll(
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<StageDirectionEntity> repository,
        [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
        [Description("Number of entries per page (0-500). 0 means every stage direction as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null)
    {
        logger.LogInformation("[Api - GetAllStageDirections] page={Page} pageSize={PageSize}", page, pageSize);

        if (!PaginationParsing.TryParse(page, pageSize, localizer, out var pageValue, out var pageSizeValue, out var pageError))
            return pageError!;

        var result = await repository.GetPageAsync(pageValue, pageSizeValue);

        var beyondLastError = PaginationParsing.ValidatePageBeyondLast(pageValue, result.TotalPages, localizer);
        if (beyondLastError is not null)
            return beyondLastError;

        var mapped = new PagedItems<StageDirectionResponse>(
            result.Items.Select(ToResponse).ToList(),
            result.Page, result.PageSize, result.TotalCount);

        return Results.Ok(mapped);
    }

    private static async Task<IResult> GetById(
        [Description("UUID of the stage direction.")] string id,
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<StageDirectionEntity> repository)
    {
        logger.LogInformation("[Api - GetStageDirectionById] id={Id}", id);

        StageDirectionEntity? entity = Guid.TryParse(id, out var stageDirectionId)
            ? await repository.GetByIdAsync(stageDirectionId)
            : null;

        var response = entity is null ? null : ToResponse(entity);
        return NotFoundResult.OkOrNotFound(response, localizer, ApiMessages.StageDirectionNotFound);
    }

    private static StageDirectionResponse ToResponse(StageDirectionEntity entity) => new()
    {
        Id                 = entity.Id.ToString("D").ToUpperInvariant(),
        Text               = entity.Text,
        ImageUrl           = entity.ImageUrl,
        CompletenessStatus = entity.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete,
    };
}

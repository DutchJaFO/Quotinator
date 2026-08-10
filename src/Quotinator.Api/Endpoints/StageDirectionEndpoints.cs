using Quotinator.Data.Enums;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Quotinator.Api.Endpoints.Shared;
using Quotinator.Core.Models;
using Quotinator.Constants.Api;
using Quotinator.Constants.RateLimiting;
using Quotinator.Core.Services;
using Quotinator.Data.Entities;
using Quotinator.Data.Helpers;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Core.Entities;
using Quotinator.Logging;

namespace Quotinator.Api.Endpoints;

/// <summary>Registers all <c>/api/v1/masterdata/stagedirections</c> endpoints.</summary>
internal static class StageDirectionEndpoints
{
    // Static classes cannot be type arguments (CS0718); this nested class is the ILogger<T> category.
    private sealed class Log { }

    // Held as consts (#279) so .WithName(...) and each handler's own logging tag can never drift
    // apart — see CLAUDE.md's "Endpoint naming convention" section.
    private const string GetAllStageDirectionsName = "GetAllStageDirections";
    private const string GetStageDirectionByIdName = "GetStageDirectionById";

    internal static void MapStageDirectionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/masterdata/stagedirections")
                       .WithTags(ApiTags.MasterData)
                       .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapGet("/", GetAll)
             .WithName(GetAllStageDirectionsName)
             .WithSummary("List stage directions")
             .WithDescription(
                 "Returns a paginated list of stage directions. Maximum `pageSize` is 500. " +
                 "`pageSize=0` returns every stage direction as a single page.");

        group.MapGet("/{id}", GetById)
             .WithName(GetStageDirectionByIdName)
             .WithSummary("Stage direction by ID")
             .WithDescription("Returns a single stage direction by ID. Matches case-insensitively. Returns 404 if not found.");
    }

    private static Task<IResult> GetAll(
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<StageDirectionEntity> repository,
        [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
        [Description("Number of entries per page (0-500). 0 means every stage direction as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null)
    {
        logger.LogPageQuery($"[Api - {GetAllStageDirectionsName}]", page, pageSize);

        return PagedListing.GetAllAsync<StageDirectionEntity, StageDirectionResponse>(
            page, pageSize, localizer, repository,
            items => Task.FromResult<IReadOnlyList<StageDirectionResponse>>([.. items.Select(ToResponse)]));
    }

    private static Task<IResult> GetById(
        [Description("UUID of the stage direction.")] string id,
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<StageDirectionEntity> repository)
    {
        logger.LogIdQuery($"[Api - {GetStageDirectionByIdName}]", id);

        return EntityLookup.TryFindByIdAsync(id, localizer, repository, ApiMessages.StageDirectionNotFound,
            entity => Task.FromResult(ToResponse(entity)));
    }

    private static StageDirectionResponse ToResponse(StageDirectionEntity entity) => new()
    {
        Id                 = entity.Id.ToCanonicalId(),
        Text               = entity.Text,
        ImageUrl           = entity.ImageUrl,
        CompletenessStatus = entity.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete,
    };
}

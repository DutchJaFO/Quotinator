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

/// <summary>Registers all <c>/api/v1/masterdata/universes</c> endpoints.</summary>
internal static class UniverseEndpoints
{
    // Static classes cannot be type arguments (CS0718); this nested class is the ILogger<T> category.
    private sealed class Log { }

    // Held as consts (#279) so .WithName(...) and each handler's own logging tag can never drift
    // apart — see CLAUDE.md's "Endpoint naming convention" section.
    private const string GetAllUniversesName = "GetAllUniverses";
    private const string GetUniverseByIdName = "GetUniverseById";

    internal static void MapUniverseEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/masterdata/universes")
                       .WithTags(ApiTags.MasterData)
                       .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapGet("/", GetAll)
             .WithName(GetAllUniversesName)
             .WithSummary("List universes")
             .WithDescription(
                 "Returns a paginated list of universes. Maximum `pageSize` is 500. " +
                 "`pageSize=0` returns every universe as a single page.");

        group.MapGet("/{id}", GetById)
             .WithName(GetUniverseByIdName)
             .WithSummary("Universe by ID")
             .WithDescription("Returns a single universe by ID. Matches case-insensitively. Returns 404 if not found.");
    }

    private static Task<IResult> GetAll(
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<UniverseEntity> repository,
        [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
        [Description("Number of entries per page (0-500). 0 means every universe as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null)
    {
        logger.LogPageQuery($"[Api - {GetAllUniversesName}]", page, pageSize);

        return PagedListing.GetAllAsync<UniverseEntity, UniverseResponse>(
            page, pageSize, localizer, repository,
            items => Task.FromResult<IReadOnlyList<UniverseResponse>>([.. items.Select(ToResponse)]));
    }

    private static Task<IResult> GetById(
        [Description("UUID of the universe.")] string id,
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<UniverseEntity> repository)
    {
        logger.LogIdQuery($"[Api - {GetUniverseByIdName}]", id);

        return EntityLookup.TryFindByIdAsync(id, localizer, repository, ApiMessages.UniverseNotFound,
            entity => Task.FromResult(ToResponse(entity)));
    }

    private static UniverseResponse ToResponse(UniverseEntity entity) => new()
    {
        Id                 = entity.Id.ToCanonicalId(),
        Name               = entity.Name,
        CompletenessStatus = entity.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete,
    };
}

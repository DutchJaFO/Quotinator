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

namespace Quotinator.Api.Endpoints;

/// <summary>Registers all <c>/api/v1/masterdata/universes</c> endpoints.</summary>
internal static class UniverseEndpoints
{
    // Static classes cannot be type arguments (CS0718); this nested class is the ILogger<T> category.
    private sealed class Log { }

    internal static void MapUniverseEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/masterdata/universes")
                       .WithTags(ApiTags.MasterData)
                       .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapGet("/", GetAll)
             .WithName("GetAllUniverses")
             .WithSummary("List universes")
             .WithDescription(
                 "Returns a paginated list of universes. Maximum `pageSize` is 500. " +
                 "`pageSize=0` returns every universe as a single page.");

        group.MapGet("/{id}", GetById)
             .WithName("GetUniverseById")
             .WithSummary("Universe by ID")
             .WithDescription("Returns a single universe by ID. Matches case-insensitively. Returns 404 if not found.");
    }

    private static async Task<IResult> GetAll(
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<UniverseEntity> repository,
        [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
        [Description("Number of entries per page (0-500). 0 means every universe as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null)
    {
        logger.LogInformation("[Api - GetAllUniverses] page={Page} pageSize={PageSize}", page, pageSize);

        if (!PaginationParsing.TryParse(page, pageSize, localizer, out var pageValue, out var pageSizeValue, out var pageError))
            return pageError!;

        var result = await repository.GetPageAsync(pageValue, pageSizeValue);

        var beyondLastError = PaginationParsing.ValidatePageBeyondLast(pageValue, result.TotalPages, localizer);
        if (beyondLastError is not null)
            return beyondLastError;

        var mapped = new PagedItems<UniverseResponse>(
            result.Items.Select(ToResponse).ToList(),
            result.Page, result.PageSize, result.TotalCount);

        return Results.Ok(mapped);
    }

    private static async Task<IResult> GetById(
        [Description("UUID of the universe.")] string id,
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<UniverseEntity> repository)
    {
        logger.LogInformation("[Api - GetUniverseById] id={Id}", id);

        UniverseEntity? entity = Guid.TryParse(id, out var universeId)
            ? await repository.GetByIdAsync(universeId)
            : null;

        var response = entity is null ? null : ToResponse(entity);
        return NotFoundResult.OkOrNotFound(response, localizer, ApiMessages.UniverseNotFound);
    }

    private static UniverseResponse ToResponse(UniverseEntity entity) => new()
    {
        Id                 = entity.Id.ToCanonicalId(),
        Name               = entity.Name,
        CompletenessStatus = entity.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete,
    };
}

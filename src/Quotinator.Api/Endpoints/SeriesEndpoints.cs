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
using Quotinator.Core.Repositories;

namespace Quotinator.Api.Endpoints;

/// <summary>Registers all <c>/api/v1/masterdata/series</c> endpoints.</summary>
internal static class SeriesEndpoints
{
    // Static classes cannot be type arguments (CS0718); this nested class is the ILogger<T> category.
    private sealed class Log { }

    internal static void MapSeriesEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/masterdata/series")
                       .WithTags(ApiTags.MasterData)
                       .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapGet("/", GetAll)
             .WithName("GetAllSeries")
             .WithSummary("List Series")
             .WithDescription(
                 "Returns a paginated list of Series, each with the Universe it belongs to (if any) as a " +
                 "minimal {id, name} reference. Maximum `pageSize` is 500; `pageSize=0` returns every " +
                 "Series as a single page.");

        group.MapGet("/{id}", GetById)
             .WithName("GetSeriesById")
             .WithSummary("Series by ID")
             .WithDescription(
                 "Returns a single Series by ID. Returns 404 if not found. `{id}` matches case-insensitively.");
    }

    private static async Task<IResult> GetAll(
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<SeriesEntity> repository,
        ISeriesUniverseReferenceReader universeReader,
        [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
        [Description("Number of entries per page (0–500). 0 means every matching entry as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null)
    {
        logger.LogInformation("[Api - GetAllSeries] page={Page} pageSize={PageSize}", page, pageSize);

        if (!PaginationParsing.TryParse(page, pageSize, localizer, out var pageValue, out var pageSizeValue, out var pageError))
            return pageError!;

        var result = await repository.GetPageAsync(pageValue, pageSizeValue);

        var beyondLast = PaginationParsing.ValidatePageBeyondLast(pageValue, result.TotalPages, localizer);
        if (beyondLast is not null)
            return beyondLast;

        var seriesIds           = result.Items.Select(s => s.Id).ToList();
        var universesBySeriesId = await universeReader.GetUniverseReferencesForManyAsync(seriesIds);

        var items = result.Items
            .Select(s => ToResponse(s, universesBySeriesId.TryGetValue(s.Id, out var universe)
                ? new MasterDataReference(universe.Id.ToCanonicalId(), universe.Name)
                : null))
            .ToList();

        var response = new PagedItems<SeriesResponse>(items, result.Page, result.PageSize, result.TotalCount);
        return Results.Ok(response);
    }

    private static async Task<IResult> GetById(
        [Description("UUID of the Series.")] string id,
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<SeriesEntity> repository,
        ISeriesUniverseReferenceReader universeReader)
    {
        logger.LogInformation("[Api - GetSeriesById] id={Id}", id);

        if (!Guid.TryParse(id, out var seriesId))
            return NotFoundResult.OkOrNotFound<SeriesResponse>(null, localizer, ApiMessages.SeriesNotFound);

        var entity = await repository.GetByIdAsync(seriesId);
        if (entity is null)
            return NotFoundResult.OkOrNotFound<SeriesResponse>(null, localizer, ApiMessages.SeriesNotFound);

        var universeRef = await universeReader.GetUniverseReferenceAsync(seriesId);
        var universe     = universeRef is { } u ? new MasterDataReference(u.Id.ToCanonicalId(), u.Name) : null;

        return NotFoundResult.OkOrNotFound(ToResponse(entity, universe), localizer, ApiMessages.SeriesNotFound);
    }

    private static SeriesResponse ToResponse(SeriesEntity entity, MasterDataReference? universe) => new()
    {
        Id                 = entity.Id.ToCanonicalId(),
        Name               = entity.Name,
        Universe           = universe,
        CompletenessStatus = entity.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete,
    };
}

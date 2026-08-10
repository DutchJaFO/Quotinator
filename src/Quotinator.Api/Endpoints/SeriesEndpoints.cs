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
using Quotinator.Core.Repositories;
using Quotinator.Logging;

namespace Quotinator.Api.Endpoints;

/// <summary>Registers all <c>/api/v1/masterdata/series</c> endpoints.</summary>
internal static class SeriesEndpoints
{
    // Static classes cannot be type arguments (CS0718); this nested class is the ILogger<T> category.
    private sealed class Log { }

    // Held as consts (#279) so .WithName(...) and each handler's own logging tag can never drift
    // apart — see CLAUDE.md's "Endpoint naming convention" section.
    private const string GetAllSeriesName = "GetAllSeries";
    private const string GetSeriesByIdName = "GetSeriesById";

    internal static void MapSeriesEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/masterdata/series")
                       .WithTags(ApiTags.MasterData)
                       .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapGet("/", GetAll)
             .WithName(GetAllSeriesName)
             .WithSummary("List series")
             .WithDescription(
                 "Returns a paginated list of Series, each with the Universe it belongs to (if any) as a " +
                 "minimal {id, name} reference. Maximum `pageSize` is 500; `pageSize=0` returns every " +
                 "Series as a single page.");

        group.MapGet("/{id}", GetById)
             .WithName(GetSeriesByIdName)
             .WithSummary("Series by ID")
             .WithDescription(
                 "Returns a single Series by ID. Returns 404 if not found. `{id}` matches case-insensitively.");
    }

    private static Task<IResult> GetAll(
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<SeriesEntity> repository,
        ISeriesUniverseReferenceReader universeReader,
        [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
        [Description("Number of entries per page (0–500). 0 means every matching entry as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null)
    {
        logger.LogPageQuery($"[Api - {GetAllSeriesName}]", page, pageSize);

        return PagedListing.GetAllAsync<SeriesEntity, SeriesResponse>(
            page, pageSize, localizer, repository,
            async items =>
            {
                var seriesIds           = items.Select(s => s.Id).ToList();
                var universesBySeriesId = await universeReader.GetUniverseReferencesForManyAsync(seriesIds);
                return [.. items.Select(s => ToResponse(s, universesBySeriesId.TryGetValue(s.Id, out var universe)
                    ? new MasterDataReference(universe.Id.ToCanonicalId(), universe.Name)
                    : null))];
            });
    }

    private static Task<IResult> GetById(
        [Description("UUID of the Series.")] string id,
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<SeriesEntity> repository,
        ISeriesUniverseReferenceReader universeReader)
    {
        logger.LogIdQuery($"[Api - {GetSeriesByIdName}]", id);

        return EntityLookup.TryFindByIdAsync(id, localizer, repository, ApiMessages.SeriesNotFound,
            async entity =>
            {
                var universeRef = await universeReader.GetUniverseReferenceAsync(entity.Id);
                var universe    = universeRef is { } u ? new MasterDataReference(u.Id.ToCanonicalId(), u.Name) : null;
                return ToResponse(entity, universe);
            });
    }

    private static SeriesResponse ToResponse(SeriesEntity entity, MasterDataReference? universe) => new()
    {
        Id                 = entity.Id.ToCanonicalId(),
        Name               = entity.Name,
        Universe           = universe,
        CompletenessStatus = entity.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete,
    };
}

using Quotinator.Data.Enums;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Quotinator.Api.Endpoints.Shared;
using Quotinator.Core.Import;
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

/// <summary>Registers all <c>/api/v1/masterdata/seasons</c> endpoints.</summary>
internal static class SeasonEndpoints
{
    // Static classes cannot be type arguments (CS0718); this nested class is the ILogger<T> category.
    private sealed class Log { }

    // Held as consts (#279) so .WithName(...) and each handler's own logging tag can never drift
    // apart — see CLAUDE.md's "Endpoint naming convention" section.
    private const string GetAllSeasonsName = "GetAllSeasons";
    private const string GetSeasonByIdName = "GetSeasonById";

    internal static void MapSeasonEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/masterdata/seasons")
                       .WithTags(ApiTags.MasterData)
                       .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapGet("/", GetAll)
             .WithName(GetAllSeasonsName)
             .WithSummary("List seasons")
             .WithDescription(
                 "Returns a paginated list of Seasons — an ordered grouping of Sources within a Series " +
                 "(#375), such as a television series' seasons — each with the Series it belongs to " +
                 "(if any) as a minimal {id, name} reference. Maximum `pageSize` is 500; `pageSize=0` " +
                 "returns every Season as a single page.");

        group.MapGet("/{id}", GetById)
             .WithName(GetSeasonByIdName)
             .WithSummary("Season by ID")
             .WithDescription(
                 "Returns a single Season by ID. Returns 404 if not found. `{id}` matches case-insensitively.");
    }

    private static Task<IResult> GetAll(
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<SeasonEntity> repository,
        ISeasonSeriesReferenceReader seriesReader,
        [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
        [Description("Number of entries per page (0–500). 0 means every matching entry as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null)
    {
        logger.LogPageQuery($"[Api - {GetAllSeasonsName}]", page, pageSize);

        return PagedListing.GetAllAsync<SeasonEntity, SeasonResponse>(
            page, pageSize, localizer, repository,
            async items =>
            {
                var seasonIds        = items.Select(s => s.Id).ToList();
                var seriesBySeasonId = await seriesReader.GetSeriesReferencesForManyAsync(seasonIds);
                return [.. items.Select(s => ToResponse(s, seriesBySeasonId.TryGetValue(s.Id, out var series)
                    ? new MasterDataReference(series.Id.ToCanonicalId(), series.Name)
                    : null))];
            });
    }

    private static Task<IResult> GetById(
        [Description("UUID of the Season.")] string id,
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<SeasonEntity> repository,
        ISeasonSeriesReferenceReader seriesReader)
    {
        logger.LogIdQuery($"[Api - {GetSeasonByIdName}]", id);

        return EntityLookup.TryFindByIdAsync(id, localizer, repository, ApiMessages.SeasonNotFound,
            async entity =>
            {
                var seriesRef = await seriesReader.GetSeriesReferenceAsync(entity.Id);
                var series    = seriesRef is { } s ? new MasterDataReference(s.Id.ToCanonicalId(), s.Name) : null;
                return ToResponse(entity, series);
            });
    }

    private static SeasonResponse ToResponse(SeasonEntity entity, MasterDataReference? series) => new()
    {
        Id                 = entity.Id.ToCanonicalId(),
        Number             = entity.Number,
        Title              = entity.Title,
        Subtitle           = entity.Subtitle,
        DisplayName        = SeasonDisplay.Format(entity.Number, entity.Title, entity.Subtitle),
        Series             = series,
        CompletenessStatus = entity.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete,
    };
}

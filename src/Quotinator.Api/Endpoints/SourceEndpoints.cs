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

/// <summary>Registers all <c>/api/v1/masterdata/sources</c> endpoints.</summary>
internal static class SourceEndpoints
{
    // Static classes cannot be type arguments (CS0718); this nested class is the ILogger<T> category.
    private sealed class Log { }

    // Held as consts (#279) so .WithName(...) and each handler's own logging tag can never drift
    // apart — see CLAUDE.md's "Endpoint naming convention" section.
    private const string GetAllSourcesName = "GetAllSources";
    private const string GetSourceByIdName = "GetSourceById";

    internal static void MapSourceEndpoints(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/masterdata/sources")
                       .WithTags(ApiTags.MasterData)
                       .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapGet("/", GetAll)
             .WithName(GetAllSourcesName)
             .WithSummary("List sources")
             .WithDescription(
                 "Returns a paginated list of Sources — the films, television series, books, and other " +
                 "works quotes are drawn from. See CLAUDE.md's \"Standard pagination contract\" for " +
                 "page/pageSize semantics.");

        group.MapGet("/{id}", GetById)
             .WithName(GetSourceByIdName)
             .WithSummary("Source by ID")
             .WithDescription("Returns a single Source by UUID. Returns 404 if not found. Matches `id` case-insensitively.");
    }

    private static Task<IResult> GetAll(
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<SourceEntity> repository,
        ISourceSeriesReferenceReader seriesReader,
        ISourceSeasonReferenceReader seasonReader,
        [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
        [Description("Number of entries per page (0–500). 0 means every matching entry as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null)
    {
        logger.LogPageQuery($"[Api - {GetAllSourcesName}]", page, pageSize);

        return PagedListing.GetAllAsync<SourceEntity, SourceResponse>(
            page, pageSize, localizer, repository,
            async items =>
            {
                List<Guid> sourceIds        = [.. items.Select(s => s.Id)];
                IReadOnlyDictionary<Guid, (Guid Id, string Name)> seriesBySourceId = await seriesReader.GetSeriesReferencesForManyAsync(sourceIds);
                IReadOnlyDictionary<Guid, (Guid Id, int Number, string? Title, string? Subtitle)> seasonBySourceId = await seasonReader.GetSeasonReferencesForManyAsync(sourceIds);
                return [.. items.Select(s => ToResponse(s,
                    seriesBySourceId.TryGetValue(s.Id, out (Guid Id, string Name) series)
                        ? new MasterDataReference(series.Id.ToCanonicalId(), series.Name)
                        : null,
                    seasonBySourceId.TryGetValue(s.Id, out (Guid Id, int Number, string? Title, string? Subtitle) season)
                        ? new MasterDataReference(season.Id.ToCanonicalId(), SeasonDisplay.Format(season.Number, season.Title, season.Subtitle))
                        : null))];
            });
    }

    private static Task<IResult> GetById(
        [Description("UUID of the source.")] string id,
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<SourceEntity> repository,
        ISourceSeriesReferenceReader seriesReader,
        ISourceSeasonReferenceReader seasonReader)
    {
        logger.LogIdQuery($"[Api - {GetSourceByIdName}]", id);

        return EntityLookup.TryFindByIdAsync(id, localizer, repository, ApiMessages.SourceNotFound,
            async source =>
            {
                (Guid Id, string Name)? seriesRef = await seriesReader.GetSeriesReferenceAsync(source.Id);
                MasterDataReference? series    = seriesRef is { } s ? new MasterDataReference(s.Id.ToCanonicalId(), s.Name) : null;
                (Guid Id, int Number, string? Title, string? Subtitle)? seasonRef = await seasonReader.GetSeasonReferenceAsync(source.Id);
                MasterDataReference? season    = seasonRef is { } se ? new MasterDataReference(se.Id.ToCanonicalId(), SeasonDisplay.Format(se.Number, se.Title, se.Subtitle)) : null;
                return ToResponse(source, series, season);
            });
    }

    private static SourceResponse ToResponse(SourceEntity source, MasterDataReference? series, MasterDataReference? season) => new()
    {
        Id                 = source.Id.ToCanonicalId(),
        Title              = source.Title,
        Type               = source.Type.Parsed?.ToString().ToLowerInvariant()
                              ?? source.Type.Raw.ToLowerInvariant(),
        Date               = string.IsNullOrEmpty(source.Date.Raw) ? null : source.Date.Raw,
        Series             = series,
        Season             = season,
        CompletenessStatus = source.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete,
    };
}

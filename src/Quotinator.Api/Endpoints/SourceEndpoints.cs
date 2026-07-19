using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Quotinator.Api.Endpoints.Shared;
using Quotinator.Core.Models;
using Quotinator.Constants.Api;
using Quotinator.Constants.RateLimiting;
using Quotinator.Core.Services;
using Quotinator.Data.Entities;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Core.Entities;
using Quotinator.Core.Repositories;

namespace Quotinator.Api.Endpoints;

/// <summary>Registers all <c>/api/v1/masterdata/sources</c> endpoints.</summary>
internal static class SourceEndpoints
{
    // Static classes cannot be type arguments (CS0718); this nested class is the ILogger<T> category.
    private sealed class Log { }

    internal static void MapSourceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/masterdata/sources")
                       .WithTags(ApiTags.MasterData)
                       .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapGet("/", GetAll)
             .WithName("GetAllSources")
             .WithSummary("List sources")
             .WithDescription(
                 "Returns a paginated list of Sources — the films, television series, books, and other " +
                 "works quotes are drawn from. See CLAUDE.md's \"Standard pagination contract\" for " +
                 "page/pageSize semantics.");

        group.MapGet("/{id}", GetById)
             .WithName("GetSourceById")
             .WithSummary("Source by ID")
             .WithDescription("Returns a single Source by UUID. Returns 404 if not found. Matches `id` case-insensitively.");
    }

    private static async Task<IResult> GetAll(
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<Source> repository,
        ISourceSeriesReferenceReader seriesReader,
        [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
        [Description("Number of entries per page (0–500). 0 means every matching entry as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null)
    {
        logger.LogInformation("[Api - GetAllSources] page={Page} pageSize={PageSize}", page, pageSize);

        if (!PaginationParsing.TryParse(page, pageSize, localizer, out var pageValue, out var pageSizeValue, out var pageError))
            return pageError!;

        var result = await repository.GetPageAsync(pageValue, pageSizeValue);

        var beyondLast = PaginationParsing.ValidatePageBeyondLast(pageValue, result.TotalPages, localizer);
        if (beyondLast is not null)
            return beyondLast;

        var sourceIds        = result.Items.Select(s => s.Id).ToList();
        var seriesBySourceId = await seriesReader.GetSeriesReferencesForManyAsync(sourceIds);

        var items = result.Items
            .Select(s => ToResponse(s, seriesBySourceId.TryGetValue(s.Id, out var series)
                ? new MasterDataReference(series.Id.ToString("D").ToUpperInvariant(), series.Name)
                : null))
            .ToList();

        var response = new PagedItems<SourceResponse>(items, result.Page, result.PageSize, result.TotalCount);
        return Results.Ok(response);
    }

    private static async Task<IResult> GetById(
        [Description("UUID of the source.")] string id,
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<Source> repository,
        ISourceSeriesReferenceReader seriesReader)
    {
        logger.LogInformation("[Api - GetSourceById] id={Id}", id);

        if (!Guid.TryParse(id, out var guid))
            return NotFoundResult.OkOrNotFound<SourceResponse>(null, localizer, ApiMessages.SourceNotFound);

        var source = await repository.GetByIdAsync(guid);
        if (source is null)
            return NotFoundResult.OkOrNotFound<SourceResponse>(null, localizer, ApiMessages.SourceNotFound);

        var seriesRef = await seriesReader.GetSeriesReferenceAsync(guid);
        var series    = seriesRef is { } s ? new MasterDataReference(s.Id.ToString("D").ToUpperInvariant(), s.Name) : null;

        return NotFoundResult.OkOrNotFound(ToResponse(source, series), localizer, ApiMessages.SourceNotFound);
    }

    private static SourceResponse ToResponse(Source source, MasterDataReference? series) => new()
    {
        Id                 = source.Id.ToString("D").ToUpperInvariant(),
        Title              = source.Title,
        Type               = source.Type.Parsed?.ToString().ToLowerInvariant()
                              ?? source.Type.Raw.ToLowerInvariant(),
        Date               = string.IsNullOrEmpty(source.Date.Raw) ? null : source.Date.Raw,
        Series             = series,
        CompletenessStatus = source.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete,
    };
}

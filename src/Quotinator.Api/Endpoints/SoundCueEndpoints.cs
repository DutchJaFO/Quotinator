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

namespace Quotinator.Api.Endpoints;

/// <summary>Registers all <c>/api/v1/masterdata/soundcues</c> endpoints.</summary>
internal static class SoundCueEndpoints
{
    // Static classes cannot be type arguments (CS0718); this nested class is the ILogger<T> category.
    private sealed class Log { }

    internal static void MapSoundCueEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/masterdata/soundcues")
                       .WithTags(ApiTags.MasterData)
                       .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapGet("/", GetAll)
             .WithName("GetAllSoundCues")
             .WithSummary("List sound cues")
             .WithDescription(
                 "Returns a paginated list of sound cues. Maximum `pageSize` is 500. " +
                 "`pageSize=0` returns every sound cue as a single page.");

        group.MapGet("/{id}", GetById)
             .WithName("GetSoundCueById")
             .WithSummary("Sound cue by ID")
             .WithDescription("Returns a single sound cue by ID. Matches case-insensitively. Returns 404 if not found.");
    }

    private static async Task<IResult> GetAll(
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<SoundCueEntity> repository,
        [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
        [Description("Number of entries per page (0-500). 0 means every sound cue as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null)
    {
        logger.LogInformation("[Api - GetAllSoundCues] page={Page} pageSize={PageSize}", page, pageSize);

        if (!PaginationParsing.TryParse(page, pageSize, localizer, out var pageValue, out var pageSizeValue, out var pageError))
            return pageError!;

        var result = await repository.GetPageAsync(pageValue, pageSizeValue);

        var beyondLastError = PaginationParsing.ValidatePageBeyondLast(pageValue, result.TotalPages, localizer);
        if (beyondLastError is not null)
            return beyondLastError;

        var mapped = new PagedItems<SoundCueResponse>(
            result.Items.Select(ToResponse).ToList(),
            result.Page, result.PageSize, result.TotalCount);

        return Results.Ok(mapped);
    }

    private static async Task<IResult> GetById(
        [Description("UUID of the sound cue.")] string id,
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<SoundCueEntity> repository)
    {
        logger.LogInformation("[Api - GetSoundCueById] id={Id}", id);

        SoundCueEntity? entity = Guid.TryParse(id, out var soundCueId)
            ? await repository.GetByIdAsync(soundCueId)
            : null;

        var response = entity is null ? null : ToResponse(entity);
        return NotFoundResult.OkOrNotFound(response, localizer, ApiMessages.SoundCueNotFound);
    }

    private static SoundCueResponse ToResponse(SoundCueEntity entity) => new()
    {
        Id                 = entity.Id.ToString("D").ToUpperInvariant(),
        Text               = entity.Text,
        SoundFileUrl       = entity.SoundFileUrl,
        ImageUrl           = entity.ImageUrl,
        CompletenessStatus = entity.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete,
    };
}

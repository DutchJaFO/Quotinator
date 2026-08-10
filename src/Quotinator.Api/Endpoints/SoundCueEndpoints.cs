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

/// <summary>Registers all <c>/api/v1/masterdata/soundcues</c> endpoints.</summary>
internal static class SoundCueEndpoints
{
    // Static classes cannot be type arguments (CS0718); this nested class is the ILogger<T> category.
    private sealed class Log { }

    // Held as consts (#279) so .WithName(...) and each handler's own logging tag can never drift
    // apart — see CLAUDE.md's "Endpoint naming convention" section.
    private const string GetAllSoundCuesName = "GetAllSoundCues";
    private const string GetSoundCueByIdName = "GetSoundCueById";

    internal static void MapSoundCueEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/masterdata/soundcues")
                       .WithTags(ApiTags.MasterData)
                       .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapGet("/", GetAll)
             .WithName(GetAllSoundCuesName)
             .WithSummary("List sound cues")
             .WithDescription(
                 "Returns a paginated list of sound cues. Maximum `pageSize` is 500. " +
                 "`pageSize=0` returns every sound cue as a single page.");

        group.MapGet("/{id}", GetById)
             .WithName(GetSoundCueByIdName)
             .WithSummary("Sound cue by ID")
             .WithDescription("Returns a single sound cue by ID. Matches case-insensitively. Returns 404 if not found.");
    }

    private static Task<IResult> GetAll(
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<SoundCueEntity> repository,
        [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
        [Description("Number of entries per page (0-500). 0 means every sound cue as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null)
    {
        logger.LogPageQuery($"[Api - {GetAllSoundCuesName}]", page, pageSize);

        return PagedListing.GetAllAsync<SoundCueEntity, SoundCueResponse>(
            page, pageSize, localizer, repository,
            items => Task.FromResult<IReadOnlyList<SoundCueResponse>>([.. items.Select(ToResponse)]));
    }

    private static Task<IResult> GetById(
        [Description("UUID of the sound cue.")] string id,
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<SoundCueEntity> repository)
    {
        logger.LogIdQuery($"[Api - {GetSoundCueByIdName}]", id);

        return EntityLookup.TryFindByIdAsync(id, localizer, repository, ApiMessages.SoundCueNotFound,
            entity => Task.FromResult(ToResponse(entity)));
    }

    private static SoundCueResponse ToResponse(SoundCueEntity entity) => new()
    {
        Id                 = entity.Id.ToCanonicalId(),
        Text               = entity.Text,
        SoundFileUrl       = entity.SoundFileUrl,
        ImageUrl           = entity.ImageUrl,
        CompletenessStatus = entity.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete,
    };
}

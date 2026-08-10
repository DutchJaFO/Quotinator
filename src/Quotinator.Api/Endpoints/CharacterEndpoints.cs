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

/// <summary>Registers all <c>/api/v1/masterdata/characters</c> endpoints.</summary>
internal static class CharacterEndpoints
{
    // Static classes cannot be type arguments (CS0718); this nested class is the ILogger<T> category.
    private sealed class Log { }

    // Held as consts (#279) so .WithName(...) and each handler's own logging tag can never drift
    // apart — see CLAUDE.md's "Endpoint naming convention" section.
    private const string GetAllCharactersName = "GetAllCharacters";
    private const string GetCharacterByIdName = "GetCharacterById";

    internal static void MapCharacterEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/masterdata/characters")
                       .WithTags(ApiTags.MasterData)
                       .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapGet("/", GetAll)
             .WithName(GetAllCharactersName)
             .WithSummary("List characters")
             .WithDescription(
                 "Returns a paginated list of characters, each with the Sources it appears in (#179) as " +
                 "minimal {id, name} references. See CLAUDE.md's \"Standard pagination contract\" for " +
                 "page/pageSize semantics.");

        group.MapGet("/{id}", GetById)
             .WithName(GetCharacterByIdName)
             .WithSummary("Character by ID")
             .WithDescription("Returns a single character with the Sources it appears in. Returns 404 if not found. Matches `id` case-insensitively.");
    }

    private static Task<IResult> GetAll(
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<CharacterEntity> repository,
        ICharacterSourceLinkReader linkReader,
        [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
        [Description("Number of entries per page (0–500). 0 means every matching entry as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null)
    {
        logger.LogPageQuery($"[Api - {GetAllCharactersName}]", page, pageSize);

        return PagedListing.GetAllAsync<CharacterEntity, CharacterResponse>(
            page, pageSize, localizer, repository,
            async items =>
            {
                var characterIds     = items.Select(c => c.Id).ToList();
                var linksByCharacter = await linkReader.GetSourceReferencesForManyAsync(characterIds);
                return [.. items.Select(c => ToResponse(c, linksByCharacter.TryGetValue(c.Id, out var sources) ? sources : []))];
            });
    }

    private static Task<IResult> GetById(
        [Description("UUID of the character.")] string id,
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<CharacterEntity> repository,
        ICharacterSourceLinkReader linkReader)
    {
        logger.LogIdQuery($"[Api - {GetCharacterByIdName}]", id);

        return EntityLookup.TryFindByIdAsync(id, localizer, repository, ApiMessages.CharacterNotFound,
            async character =>
            {
                var sources = await linkReader.GetSourceReferencesAsync(character.Id);
                return ToResponse(character, sources);
            });
    }

    private static CharacterResponse ToResponse(CharacterEntity character, IReadOnlyList<(Guid Id, string Name)> sources) => new()
    {
        Id                 = character.Id.ToCanonicalId(),
        Name               = character.Name,
        CompletenessStatus = character.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete,
        Sources            = [.. sources.Select(s => new MasterDataReference(s.Id.ToCanonicalId(), s.Name))],
    };
}

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

/// <summary>Registers all <c>/api/v1/masterdata/characters</c> endpoints.</summary>
internal static class CharacterEndpoints
{
    // Static classes cannot be type arguments (CS0718); this nested class is the ILogger<T> category.
    private sealed class Log { }

    internal static void MapCharacterEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/masterdata/characters")
                       .WithTags(ApiTags.MasterData)
                       .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapGet("/", GetAll)
             .WithName("GetAllCharacters")
             .WithSummary("List characters")
             .WithDescription(
                 "Returns a paginated list of characters, each with the Sources it appears in (#179) as " +
                 "minimal {id, name} references. See CLAUDE.md's \"Standard pagination contract\" for " +
                 "page/pageSize semantics.");

        group.MapGet("/{id}", GetById)
             .WithName("GetCharacterById")
             .WithSummary("Character by ID")
             .WithDescription("Returns a single character with the Sources it appears in. Returns 404 if not found. Matches `id` case-insensitively.");
    }

    private static async Task<IResult> GetAll(
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<Character> repository,
        ICharacterSourceLinkReader linkReader,
        [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
        [Description("Number of entries per page (0–500). 0 means every matching entry as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null)
    {
        logger.LogInformation("[Api - GetAllCharacters] page={Page} pageSize={PageSize}", page, pageSize);

        if (!PaginationParsing.TryParse(page, pageSize, localizer, out var pageValue, out var pageSizeValue, out var pageError))
            return pageError!;

        var result = await repository.GetPageAsync(pageValue, pageSizeValue);

        var beyondLast = PaginationParsing.ValidatePageBeyondLast(pageValue, result.TotalPages, localizer);
        if (beyondLast is not null)
            return beyondLast;

        var characterIds     = result.Items.Select(c => c.Id).ToList();
        var linksByCharacter = await linkReader.GetSourceReferencesForManyAsync(characterIds);

        var items = result.Items
            .Select(c => ToResponse(c, linksByCharacter.TryGetValue(c.Id, out var sources) ? sources : []))
            .ToList();

        var response = new PagedItems<CharacterResponse>(items, result.Page, result.PageSize, result.TotalCount);
        return Results.Ok(response);
    }

    private static async Task<IResult> GetById(
        [Description("UUID of the character.")] string id,
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<Character> repository,
        ICharacterSourceLinkReader linkReader)
    {
        logger.LogInformation("[Api - GetCharacterById] id={Id}", id);

        if (!Guid.TryParse(id, out var characterId))
            return NotFoundResult.OkOrNotFound<CharacterResponse>(null, localizer, ApiMessages.CharacterNotFound);

        var character = await repository.GetByIdAsync(characterId);
        if (character is null)
            return NotFoundResult.OkOrNotFound<CharacterResponse>(null, localizer, ApiMessages.CharacterNotFound);

        var sources = await linkReader.GetSourceReferencesAsync(characterId);
        return NotFoundResult.OkOrNotFound(ToResponse(character, sources), localizer, ApiMessages.CharacterNotFound);
    }

    private static CharacterResponse ToResponse(Character character, IReadOnlyList<(Guid Id, string Name)> sources) => new()
    {
        Id                 = character.Id.ToString("D").ToUpperInvariant(),
        Name               = character.Name,
        CompletenessStatus = character.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete,
        Sources            = sources.Select(s => new MasterDataReference(s.Id.ToString("D").ToUpperInvariant(), s.Name)).ToList(),
    };
}

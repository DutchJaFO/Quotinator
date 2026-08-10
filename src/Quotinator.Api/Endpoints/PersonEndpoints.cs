using Quotinator.Data.Enums;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Quotinator.Api.Endpoints.Shared;
using Quotinator.Core.Models;
using Quotinator.Constants.Api;
using Quotinator.Constants.RateLimiting;
using Quotinator.Core.Services;
using Quotinator.Data.Helpers;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Core.Entities;
using Quotinator.Logging;

namespace Quotinator.Api.Endpoints;

/// <summary>Registers all <c>/api/v1/masterdata/people</c> endpoints.</summary>
internal static class PersonEndpoints
{
    // Static classes cannot be type arguments (CS0718); this nested class is the ILogger<T> category.
    private sealed class Log { }

    // Held as consts (#279) so .WithName(...) and each handler's own logging tag can never drift
    // apart — see CLAUDE.md's "Endpoint naming convention" section.
    private const string GetAllPeopleName = "GetAllPeople";
    private const string GetPersonByIdName = "GetPersonById";

    internal static void MapPersonEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/masterdata/people")
                       .WithTags(ApiTags.MasterData)
                       .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapGet("/", GetAll)
             .WithName(GetAllPeopleName)
             .WithSummary("List people")
             .WithDescription(
                 "Returns a paginated list of people (real individuals who said or wrote a quote). " +
                 "No entity-specific filters yet.");

        group.MapGet("/{id}", GetById)
             .WithName(GetPersonByIdName)
             .WithSummary("Person by ID")
             .WithDescription(
                 "Returns a single person by UUID. Returns 404 if not found. `{id}` matches case-insensitively.");
    }

    private static Task<IResult> GetAll(
        IApiLocalizer localizer,
        ILogger<Log> logger,
        [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
        [Description("Number of entries per page (0–500). 0 means every matching entry as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null,
        IListableRepository<PersonEntity> repository = null!)
    {
        logger.LogPageQuery($"[Api - {GetAllPeopleName}]", page, pageSize);

        return PagedListing.GetAllAsync<PersonEntity, PersonResponse>(
            page, pageSize, localizer, repository,
            items => Task.FromResult<IReadOnlyList<PersonResponse>>([.. items.Select(ToResponse)]));
    }

    private static Task<IResult> GetById(
        [Description("UUID of the person.")] string id,
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<PersonEntity> repository)
    {
        logger.LogIdQuery($"[Api - {GetPersonByIdName}]", id);

        return EntityLookup.TryFindByIdAsync(id, localizer, repository, ApiMessages.PersonNotFound,
            person => Task.FromResult(ToResponse(person)));
    }

    private static PersonResponse ToResponse(PersonEntity person) => new()
    {
        Id                 = person.Id.ToCanonicalId(),
        Name               = person.Name,
        DateOfBirth        = string.IsNullOrEmpty(person.DateOfBirth.Raw) ? null : person.DateOfBirth.Raw,
        DateOfDeath        = string.IsNullOrEmpty(person.DateOfDeath.Raw) ? null : person.DateOfDeath.Raw,
        CompletenessStatus = person.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete,
    };
}

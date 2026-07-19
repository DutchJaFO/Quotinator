using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Quotinator.Api.Endpoints.Shared;
using Quotinator.Core.Models;
using Quotinator.Constants.Api;
using Quotinator.Constants.RateLimiting;
using Quotinator.Core.Services;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Core.Entities;

namespace Quotinator.Api.Endpoints;

/// <summary>Registers all <c>/api/v1/masterdata/people</c> endpoints.</summary>
internal static class PersonEndpoints
{
    // Static classes cannot be type arguments (CS0718); this nested class is the ILogger<T> category.
    private sealed class Log { }

    internal static void MapPersonEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/masterdata/people")
                       .WithTags(ApiTags.MasterData)
                       .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapGet("/", GetAll)
             .WithName("GetAllPeople")
             .WithSummary("All people (paginated)")
             .WithDescription(
                 "Returns a paginated list of people (real individuals who said or wrote a quote). " +
                 "No entity-specific filters yet.");

        group.MapGet("/{id}", GetById)
             .WithName("GetPersonById")
             .WithSummary("Person by ID")
             .WithDescription(
                 "Returns a single person by UUID. Returns 404 if not found. `{id}` matches case-insensitively.");
    }

    private static async Task<IResult> GetAll(
        IApiLocalizer localizer,
        ILogger<Log> logger,
        [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
        [Description("Number of entries per page (0–500). 0 means every matching entry as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null,
        IListableRepository<Person> repository = null!)
    {
        logger.LogInformation("[Api - GetAllPeople] page={Page} pageSize={PageSize}", page, pageSize);

        if (!PaginationParsing.TryParse(page, pageSize, localizer, out var pageValue, out var pageSizeValue, out var pageError))
            return pageError!;

        var result = await repository.GetPageAsync(pageValue, pageSizeValue);
        var response = new PagedItems<PersonResponse>(
            result.Items.Select(ToResponse).ToList(), result.Page, result.PageSize, result.TotalCount);

        return PaginationParsing.ValidatePageBeyondLast(pageValue, response.TotalPages, localizer)
            ?? Results.Ok(response);
    }

    private static async Task<IResult> GetById(
        [Description("UUID of the person.")] string id,
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<Person> repository)
    {
        logger.LogInformation("[Api - GetPersonById] id={Id}", id);

        PersonResponse? response = Guid.TryParse(id, out var personId)
            ? await repository.GetByIdAsync(personId) is { } person ? ToResponse(person) : null
            : null;

        return NotFoundResult.OkOrNotFound(response, localizer, ApiMessages.PersonNotFound);
    }

    private static PersonResponse ToResponse(Person person) => new()
    {
        Id                 = person.Id.ToString("D").ToUpperInvariant(),
        Name               = person.Name,
        DateOfBirth        = string.IsNullOrEmpty(person.DateOfBirth.Raw) ? null : person.DateOfBirth.Raw,
        DateOfDeath        = string.IsNullOrEmpty(person.DateOfDeath.Raw) ? null : person.DateOfDeath.Raw,
        CompletenessStatus = person.CompletenessStatus.Parsed
    };
}

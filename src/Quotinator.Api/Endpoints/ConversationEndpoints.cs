using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Quotinator.Api.Endpoints.Shared;
using Quotinator.Core.Models;
using Quotinator.Constants.Api;
using Quotinator.Constants.RateLimiting;
using Quotinator.Core.Helpers;
using Quotinator.Core.Services;
using Quotinator.Data.Entities;
using Quotinator.Data.Helpers;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Core.Entities;
using Quotinator.Core.Repositories;

namespace Quotinator.Api.Endpoints;

/// <summary>Registers all <c>/api/v1/conversations</c> endpoints.</summary>
internal static class ConversationEndpoints
{
    // Static classes cannot be type arguments (CS0718); this nested class is the ILogger<T> category.
    private sealed class Log { }

    internal static void MapConversationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/conversations")
                       .WithTags(ApiTags.Conversations)
                       .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapGet("/", GetAll)
             .WithName("GetAllConversations")
             .WithSummary("List conversations")
             .WithDescription(
                 "Returns a paginated list of conversation summaries — Id, Description, CompletenessStatus, and " +
                 "line count. Fetch the full ordered line list via GET /{id}. See CLAUDE.md's \"Standard " +
                 "pagination contract\" for page/pageSize semantics.");

        group.MapGet("/{id}", GetById)
             .WithName("GetConversationById")
             .WithSummary("Conversation by ID")
             .WithDescription(
                 "Returns a conversation's full ordered line list — quotes, stage directions, and sound cues. " +
                 "Returns 404 if not found. Use `lang` to request a specific language for stage direction " +
                 "and sound cue text (falls back to the original language when no translation exists); " +
                 "embedded quotes respect the same `lang` value independently.");
    }

    private static async Task<IResult> GetAll(
        IApiLocalizer localizer,
        ILogger<Log> logger,
        IListableRepository<ConversationEntity> repository,
        IConversationLineCountReader lineCountReader,
        [Description("Page number, 1-based."), DefaultValue(QueryParamDefaults.Page)] string? page = null,
        [Description("Number of entries per page (0–500). 0 means every matching entry as a single page."), DefaultValue(QueryParamDefaults.PageSize)] string? pageSize = null)
    {
        logger.LogInformation("[Api - GetAllConversations] page={Page} pageSize={PageSize}", page, pageSize);

        if (!PaginationParsing.TryParse(page, pageSize, localizer, out var pageValue, out var pageSizeValue, out var pageError))
            return pageError!;

        var result = await repository.GetPageAsync(pageValue, pageSizeValue);

        var beyondLast = PaginationParsing.ValidatePageBeyondLast(pageValue, result.TotalPages, localizer);
        if (beyondLast is not null)
            return beyondLast;

        var conversationIds = result.Items.Select(c => c.Id).ToList();
        var lineCountsById  = await lineCountReader.GetLineCountsForManyAsync(conversationIds);

        var items = result.Items
            .Select(c => ToSummaryResponse(c, lineCountsById.TryGetValue(c.Id, out var count) ? count : 0))
            .ToList();

        var response = new PagedItems<ConversationSummaryResponse>(items, result.Page, result.PageSize, result.TotalCount);
        return Results.Ok(response);
    }

    private static IResult GetById(
        [Description("UUID of the conversation.")] string id,
        IQuoteService service,
        IApiLocalizer localizer,
        ILogger<Log> logger,
        [Description("ISO 639-1 language code (e.g. `nl`, `de`). Falls back to the original language when no translation exists."), DefaultValue("en")] string? lang = null)
    {
        logger.LogInformation("[Api - GetConversationById] id={Id} lang={Lang}", id, lang);

        if (lang is not null && !InputValidation.IsValidLang(lang))
            return Results.Problem(
                detail: localizer[ApiMessages.LangInvalid],
                statusCode: StatusCodes.Status400BadRequest);

        var conversation = service.GetConversation(id, lang);
        return NotFoundResult.OkOrNotFound(conversation, localizer, ApiMessages.ConversationNotFound);
    }

    private static ConversationSummaryResponse ToSummaryResponse(ConversationEntity entity, int lineCount) => new()
    {
        Id                 = entity.Id.ToCanonicalId(),
        Description        = entity.Description,
        CompletenessStatus = entity.CompletenessStatus.Parsed ?? CompletenessStatus.Incomplete,
        LineCount          = lineCount,
    };
}

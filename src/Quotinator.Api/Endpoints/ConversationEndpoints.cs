using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Quotinator.Api.Endpoints.Shared;
using Quotinator.Constants.Api;
using Quotinator.Constants.RateLimiting;
using Quotinator.Core.Helpers;
using Quotinator.Core.Services;

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

        group.MapGet("/{id}", GetById)
             .WithName("GetConversationById")
             .WithSummary("Conversation by ID")
             .WithDescription(
                 "Returns a conversation's full ordered line list — quotes, stage directions, and sound cues. " +
                 "Returns 404 if not found. Use `lang` to request a specific language for stage direction " +
                 "and sound cue text (falls back to the original language when no translation exists); " +
                 "embedded quotes respect the same `lang` value independently.");
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
}

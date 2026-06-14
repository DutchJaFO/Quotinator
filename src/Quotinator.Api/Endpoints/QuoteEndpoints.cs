using System.ComponentModel;
using Quotinator.Api.Services;
using Quotinator.Core.Services;

namespace Quotinator.Api.Endpoints;

/// <summary>Registers all <c>/api/v1/quotes</c> endpoints.</summary>
internal static class QuoteEndpoints
{
    private const int MaxQueryLength = 200;

    internal static void MapQuoteEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/quotes")
                       .WithTags(ApiTags.Quotes)
                       .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapGet("/random", GetRandom)
             .WithName("GetRandomQuotes")
             .WithSummary("Random quote(s)")
             .WithDescription(
                 "Returns one random quote when called without parameters, " +
                 "or an array of `n` random quotes when `n` is specified (1–100). " +
                 "Use `lang` (ISO 639-1) to request a specific language; falls back to the original language if no translation exists.");

        // /search must be registered before /{id} so the literal segment wins.
        group.MapGet("/search", Search)
             .WithName("SearchQuotes")
             .WithSummary("Search quotes")
             .WithDescription(
                 "Returns quotes whose text, source, character, or author contain `q` (case-insensitive). " +
                 "Optionally filter by `type` or `genre`. Use `lang` to request a specific language.");

        group.MapGet("/{id}", GetById)
             .WithName("GetQuoteById")
             .WithSummary("Quote by ID")
             .WithDescription(
                 "Returns a single quote by its UUID. Returns 404 if not found. " +
                 "Use `lang` to request a specific language.");

        group.MapGet("/", GetAll)
             .WithName("GetAllQuotes")
             .WithSummary("All quotes (paginated)")
             .WithDescription(
                 "Returns a paginated list of all quotes. " +
                 "Optionally filter by `type` (movie, tv, anime, book, person) or `genre`. " +
                 "Use `lang` to request a specific language.");
    }

    // Returns a 400 problem result when lang or type are invalid, null when both are fine.
    // Note: lang validation errors use Accept-Language for the error message language,
    // since ?lang is invalid and cannot be used to localise the error itself.
    private static IResult? ValidateCommon(IApiLocalizer localizer, string? lang, string? type)
    {
        if (lang is not null && !InputValidation.IsValidLang(lang))
            return Results.Problem(
                detail: localizer[ApiMessages.LangInvalid],
                statusCode: StatusCodes.Status400BadRequest);

        if (type is not null && !InputValidation.ValidTypes.Contains(type.ToLowerInvariant()))
            return Results.Problem(
                detail: localizer[ApiMessages.TypeInvalid],
                statusCode: StatusCodes.Status400BadRequest);

        return null;
    }

    private static IResult GetRandom(
        IQuoteService service,
        IApiLocalizer localizer,
        [Description("Number of quotes to return (1–100). Omit for a single random quote.")] string? n = null,
        [Description("ISO 639-1 language code (e.g. `nl`, `de`). Falls back to the original language when no translation exists."), DefaultValue("en")] string? lang = null)
    {
        if (ValidateCommon(localizer, lang, null) is { } err) return err;

        if (n is null)
            return Results.Ok(service.GetRandom(1, lang).FirstOrDefault());

        if (!int.TryParse(n, out var count) || count < 1 || count > 100)
            return Results.Problem(
                detail: localizer[ApiMessages.RandomNOutOfRange],
                statusCode: StatusCodes.Status400BadRequest);

        return Results.Ok(service.GetRandom(count, lang));
    }

    private static IResult GetById(
        [Description("UUID of the quote.")] string id,
        IQuoteService service,
        IApiLocalizer localizer,
        [Description("ISO 639-1 language code (e.g. `nl`, `de`). Falls back to the original language when no translation exists."), DefaultValue("en")] string? lang = null)
    {
        if (ValidateCommon(localizer, lang, null) is { } err) return err;

        var quote = service.GetById(id, lang);
        return quote is null
            ? Results.Problem(
                detail: localizer[ApiMessages.QuoteNotFound],
                statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(quote);
    }

    private static IResult Search(
        IQuoteService service,
        IApiLocalizer localizer,
        [Description("Search term. Matched case-insensitively against quote text, source, character name, and author.")] string? q = null,
        [Description("Maximum number of results to return (1–100)."), DefaultValue(20)] string? limit = null,
        [Description("Filter by type. One of: `movie`, `tv`, `anime`, `book`, `person`.")] string? type = null,
        [Description("Filter by genre tag (e.g. `sci-fi`, `drama`, `non-fiction`).")] string? genre = null,
        [Description("ISO 639-1 language code (e.g. `nl`, `de`). Falls back to the original language when no translation exists."), DefaultValue("en")] string? lang = null)
    {
        if (ValidateCommon(localizer, lang, type) is { } err) return err;

        if (string.IsNullOrWhiteSpace(q))
            return Results.Problem(
                detail: localizer[ApiMessages.SearchQueryRequired],
                statusCode: StatusCodes.Status400BadRequest);

        if (q.Length > MaxQueryLength)
            return Results.Problem(
                detail: localizer[ApiMessages.SearchQueryTooLong],
                statusCode: StatusCodes.Status400BadRequest);

        var limitValue = 20;
        if (limit is not null && (!int.TryParse(limit, out limitValue) || limitValue < 1 || limitValue > 100))
            return Results.Problem(
                detail: localizer[ApiMessages.LimitOutOfRange],
                statusCode: StatusCodes.Status400BadRequest);

        return Results.Ok(service.Search(q, limitValue, type, genre, lang));
    }

    private static IResult GetAll(
        IQuoteService service,
        IApiLocalizer localizer,
        [Description("Page number, 1-based."), DefaultValue(1)] string? page = null,
        [Description("Number of quotes per page (1–100)."), DefaultValue(20)] string? pageSize = null,
        [Description("Filter by type. One of: `movie`, `tv`, `anime`, `book`, `person`.")] string? type = null,
        [Description("Filter by genre tag (e.g. `sci-fi`, `drama`, `non-fiction`).")] string? genre = null,
        [Description("ISO 639-1 language code (e.g. `nl`, `de`). Falls back to the original language when no translation exists."), DefaultValue("en")] string? lang = null)
    {
        if (ValidateCommon(localizer, lang, type) is { } err) return err;

        var pageValue = 1;
        if (page is not null && (!int.TryParse(page, out pageValue) || pageValue < 1))
            return Results.Problem(
                detail: localizer[ApiMessages.PageOutOfRange],
                statusCode: StatusCodes.Status400BadRequest);

        var pageSizeValue = 20;
        if (pageSize is not null && (!int.TryParse(pageSize, out pageSizeValue) || pageSizeValue < 1 || pageSizeValue > 100))
            return Results.Problem(
                detail: localizer[ApiMessages.PageSizeOutOfRange],
                statusCode: StatusCodes.Status400BadRequest);

        return Results.Ok(service.GetAll(pageValue, pageSizeValue, type, genre, lang));
    }
}

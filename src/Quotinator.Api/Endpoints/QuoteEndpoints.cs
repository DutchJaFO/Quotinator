using System.ComponentModel;
using Quotinator.Constants;
using Quotinator.Core.Helpers;
using Quotinator.Core.Models;
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
                 "Returns a result envelope containing one or more randomly selected quotes. " +
                 "Use `n` to request up to 100 quotes. " +
                 "Filter the pool with `type` (repeatable, OR logic), `genre` (repeatable, OR logic), " +
                 "`character`, `author`, or `source` (case-insensitive contains match), " +
                 "`yearFrom` / `yearTo` (inclusive year range), `year` (exact year), or `decade` (e.g. `1980` for 1980–1989, must be divisible by 10). " +
                 "Multiple distinct filter parameters combine with AND logic. " +
                 "The envelope always includes `status`, `items`, and `totalMatching` (pool size before random selection). " +
                 "Use `lang` (ISO 639-1) to request a specific language.");

        // /search must be registered before /{id} so the literal segment wins.
        group.MapGet("/search", Search)
             .WithName("SearchQuotes")
             .WithSummary("Search quotes")
             .WithDescription(
                 "Returns quotes whose text, source, character, or author contain `q` (case-insensitive). " +
                 "Optionally filter by `type` or `genre` (both repeatable, OR logic within each), " +
                 "or by `yearFrom` / `yearTo` (inclusive year range), `year` (exact year), or `decade` (must be divisible by 10). " +
                 "Use `lang` to request a specific language.");

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
                 "Optionally filter by `type` (movie, tv, anime, book, person) or `genre` — both parameters are repeatable (OR logic within each). " +
                 "Also supports `yearFrom` / `yearTo` (inclusive year range), `year` (exact year), or `decade` (must be divisible by 10). " +
                 "Use `lang` to request a specific language.");
    }

    // Returns a 400 problem result when lang or field are invalid, null when both are fine.
    private static IResult? ValidateCommon(IApiLocalizer localizer, string? lang, string? field = null)
    {
        if (lang is not null && !InputValidation.IsValidLang(lang))
            return Results.Problem(
                detail: localizer[ApiMessages.LangInvalid],
                statusCode: StatusCodes.Status400BadRequest);

        if (field is not null && !InputValidation.ValidSearchFields.Contains(field.ToLowerInvariant()))
            return Results.Problem(
                detail: localizer[ApiMessages.FieldInvalid],
                statusCode: StatusCodes.Status400BadRequest);

        return null;
    }

    // Validates type[], genre[], and text filter params for /random.
    // Returns a FilteredQuoteResult envelope when validation fails, null when all pass.
    private static FilteredQuoteResult<QuoteResponse>? ValidateFilterParams(
        IApiLocalizer localizer,
        string[]? types,
        string[]? genres,
        string? character,
        string? author,
        string? source)
    {
        if (types is not null)
        {
            var unknown = types.FirstOrDefault(t => !InputValidation.ValidTypes.Contains(t.ToLowerInvariant()));
            if (unknown is not null)
                return FilterEnvelope(FilteredResultStatus.InvalidType, localizer[ApiMessages.TypeInvalid]);
        }

        if (genres is not null)
        {
            var unknown = genres.FirstOrDefault(g => !InputValidation.ValidGenres.Contains(g.ToLowerInvariant()));
            if (unknown is not null)
                return FilterEnvelope(FilteredResultStatus.InvalidGenre, localizer[ApiMessages.GenreInvalid]);
        }

        foreach (var value in new[] { character, author, source }.Where(v => v is not null).Cast<string>())
        {
            if (value.Length > InputValidation.MaxFilterLength)
                return FilterEnvelope(FilteredResultStatus.InputTooLong, localizer[ApiMessages.FilterInputTooLong]);
            if (InputValidation.IsSuspiciousInput(value))
                return FilterEnvelope(FilteredResultStatus.InvalidInput, localizer[ApiMessages.FilterInputInvalid]);
        }

        return null;
    }

    private static FilteredQuoteResult<QuoteResponse> FilterEnvelope(FilteredResultStatus status, string message) =>
        new() { Status = status, Message = message };

    private static IResult GetRandom(
        IQuoteService service,
        IApiLocalizer localizer,
        [Description("Number of quotes to return (1–100). Omit for a single random quote.")] string? n = null,
        [Description("ISO 639-1 language code (e.g. `nl`, `de`). Falls back to the original language when no translation exists."), DefaultValue("en")] string? lang = null,
        [Description("Filter by source type (repeatable). One of: `movie`, `tv`, `anime`, `book`, `person`. Multiple values use OR logic.")] string[]? type = null,
        [Description("Filter by genre tag (repeatable, e.g. `sci-fi`, `drama`). Multiple values use OR logic.")] string[]? genre = null,
        [Description("Filter to quotes whose character field contains this value (case-insensitive).")] string? character = null,
        [Description("Filter to quotes whose author field contains this value (case-insensitive).")] string? author = null,
        [Description("Filter to quotes whose source field contains this value (case-insensitive).")] string? source = null,
        [Description("Return only quotes from this year or later (inclusive).")] int? yearFrom = null,
        [Description("Return only quotes from this year or earlier (inclusive).")] int? yearTo = null,
        [Description("Shorthand for yearFrom=N&yearTo=N — matches quotes from exactly this year.")] int? year = null,
        [Description("Shorthand for yearFrom=N&yearTo=N+9 — e.g. `1980` matches 1980–1989. Must be divisible by 10.")] int? decade = null)
    {
        if (ValidateCommon(localizer, lang) is { } err) return err;

        var count = 1;
        if (n is not null && (!int.TryParse(n, out count) || count < 1 || count > 100))
            return Results.Problem(
                detail: localizer[ApiMessages.RandomNOutOfRange],
                statusCode: StatusCodes.Status400BadRequest);

        if (decade is not null)
        {
            if (decade % 10 != 0)
                return Results.Ok(FilterEnvelope(FilteredResultStatus.InvalidInput, localizer[ApiMessages.DecadeInvalid]));
            yearFrom = decade;
            yearTo   = decade + 9;
        }
        else if (year is not null)
        {
            yearFrom = year;
            yearTo   = year;
        }

        if (yearFrom is not null && yearTo is not null && yearFrom > yearTo)
            return Results.Ok(FilterEnvelope(FilteredResultStatus.InvalidInput, localizer[ApiMessages.YearRangeInvalid]));

        if (ValidateFilterParams(localizer, type, genre, character, author, source) is { } invalid)
            return Results.Ok(invalid);

        var result = service.GetRandom(count, type, genre, character, author, source, lang, yearFrom, yearTo);

        if (result.Status == FilteredResultStatus.NoResults)
            return Results.Ok(new FilteredQuoteResult<QuoteResponse>
            {
                Status        = FilteredResultStatus.NoResults,
                Items         = [],
                TotalMatching = 0,
                Message       = localizer[ApiMessages.NoQuotesMatchFilters],
            });

        return Results.Ok(result);
    }

    private static IResult GetById(
        [Description("UUID of the quote.")] string id,
        IQuoteService service,
        IApiLocalizer localizer,
        [Description("ISO 639-1 language code (e.g. `nl`, `de`). Falls back to the original language when no translation exists."), DefaultValue("en")] string? lang = null)
    {
        if (ValidateCommon(localizer, lang) is { } err) return err;

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
        [Description("Search term. Matched case-insensitively against the selected field (or all fields when `field` is omitted).")] string? q = null,
        [Description("Maximum number of results to return (1–100)."), DefaultValue(20)] string? limit = null,
        [Description("Filter by type (repeatable). One of: `movie`, `tv`, `anime`, `book`, `person`. Multiple values use OR logic.")] string[]? type = null,
        [Description("Filter by genre tag (repeatable, e.g. `sci-fi`, `drama`). Multiple values use OR logic.")] string[]? genre = null,
        [Description("ISO 639-1 language code (e.g. `nl`, `de`). Falls back to the original language when no translation exists."), DefaultValue("en")] string? lang = null,
        [Description("Restrict search to a specific field. One of: `quote`, `source`, `character`, `author`. Omit to search all fields.")] string? field = null,
        [Description("Return only quotes from this year or later (inclusive).")] int? yearFrom = null,
        [Description("Return only quotes from this year or earlier (inclusive).")] int? yearTo = null,
        [Description("Shorthand for yearFrom=N&yearTo=N — matches quotes from exactly this year.")] int? year = null,
        [Description("Shorthand for yearFrom=N&yearTo=N+9 — e.g. `1980` matches 1980–1989. Must be divisible by 10.")] int? decade = null)
    {
        if (ValidateCommon(localizer, lang, field) is { } err) return err;

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

        if (decade is not null)
        {
            if (decade % 10 != 0)
                return Results.Problem(detail: localizer[ApiMessages.DecadeInvalid], statusCode: StatusCodes.Status400BadRequest);
            yearFrom = decade;
            yearTo   = decade + 9;
        }
        else if (year is not null)
        {
            yearFrom = year;
            yearTo   = year;
        }

        if (yearFrom is not null && yearTo is not null && yearFrom > yearTo)
            return Results.Problem(detail: localizer[ApiMessages.YearRangeInvalid], statusCode: StatusCodes.Status400BadRequest);

        return Results.Ok(service.Search(q, limitValue, type, genre, lang, field?.ToLowerInvariant(), yearFrom, yearTo));
    }

    private static IResult GetAll(
        IQuoteService service,
        IApiLocalizer localizer,
        [Description("Page number, 1-based."), DefaultValue(1)] string? page = null,
        [Description("Number of quotes per page (1–100)."), DefaultValue(20)] string? pageSize = null,
        [Description("Filter by type (repeatable). One of: `movie`, `tv`, `anime`, `book`, `person`. Multiple values use OR logic.")] string[]? type = null,
        [Description("Filter by genre tag (repeatable, e.g. `sci-fi`, `drama`). Multiple values use OR logic.")] string[]? genre = null,
        [Description("ISO 639-1 language code (e.g. `nl`, `de`). Falls back to the original language when no translation exists."), DefaultValue("en")] string? lang = null,
        [Description("Return only quotes from this year or later (inclusive).")] int? yearFrom = null,
        [Description("Return only quotes from this year or earlier (inclusive).")] int? yearTo = null,
        [Description("Shorthand for yearFrom=N&yearTo=N — matches quotes from exactly this year.")] int? year = null,
        [Description("Shorthand for yearFrom=N&yearTo=N+9 — e.g. `1980` matches 1980–1989. Must be divisible by 10.")] int? decade = null)
    {
        if (ValidateCommon(localizer, lang) is { } err) return err;

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

        if (decade is not null)
        {
            if (decade % 10 != 0)
                return Results.Problem(detail: localizer[ApiMessages.DecadeInvalid], statusCode: StatusCodes.Status400BadRequest);
            yearFrom = decade;
            yearTo   = decade + 9;
        }
        else if (year is not null)
        {
            yearFrom = year;
            yearTo   = year;
        }

        if (yearFrom is not null && yearTo is not null && yearFrom > yearTo)
            return Results.Problem(detail: localizer[ApiMessages.YearRangeInvalid], statusCode: StatusCodes.Status400BadRequest);

        return Results.Ok(service.GetAll(pageValue, pageSizeValue, type, genre, lang, yearFrom, yearTo));
    }
}

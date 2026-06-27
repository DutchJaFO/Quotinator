using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Quotinator.Constants.Api;
using Quotinator.Constants.RateLimiting;
using Quotinator.Core.Helpers;
using Quotinator.Core.Models;
using Quotinator.Core.Services;

namespace Quotinator.Api.Endpoints;

/// <summary>Registers all <c>/api/v1/quotes</c> endpoints.</summary>
internal static class QuoteEndpoints
{
    private const int MaxQueryLength = 200;

    // Static classes cannot be type arguments (CS0718); this nested class is the ILogger<T> category.
    private sealed class Log { }

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

    // Maps a validation envelope to the correct HTTP status code.
    // Semantic errors (unrecognised value) → 422; structural errors (too long, suspicious) → 400.
    private static IResult ToValidationResult(FilteredQuoteResult<QuoteResponse> envelope) =>
        Results.Json(envelope, statusCode: envelope.Status switch
        {
            FilteredResultStatus.InvalidType  => StatusCodes.Status422UnprocessableEntity,
            FilteredResultStatus.InvalidGenre => StatusCodes.Status422UnprocessableEntity,
            _                                 => StatusCodes.Status400BadRequest,
        });

    // Parses a nullable string year param to int? without throwing.
    // Returns false when the string is present but cannot be parsed as an integer.
    private static bool TryParseYear(string? raw, out int? value)
    {
        if (raw is null) { value = null; return true; }
        if (int.TryParse(raw, out var parsed)) { value = parsed; return true; }
        value = null;
        return false;
    }

    private static IResult YearParseError(IApiLocalizer localizer, string paramName) =>
        Results.Problem(
            detail: string.Format(localizer[ApiMessages.YearParamNotInteger], paramName),
            statusCode: StatusCodes.Status422UnprocessableEntity);

    private static IResult GetRandom(
        IQuoteService service,
        IApiLocalizer localizer,
        ILogger<Log> logger,
        [Description("Number of quotes to return (1–100). Omit for a single random quote.")] string? n = null,
        [Description("ISO 639-1 language code (e.g. `nl`, `de`). Falls back to the original language when no translation exists."), DefaultValue("en")] string? lang = null,
        [Description("Filter by source type (repeatable). One of: `movie`, `tv`, `anime`, `book`, `person`. Multiple values use OR logic.")] string[]? type = null,
        [Description("Filter by genre tag (repeatable, e.g. `sci-fi`, `drama`). Multiple values use OR logic.")] string[]? genre = null,
        [Description("Filter to quotes whose character field contains this value (case-insensitive).")] string? character = null,
        [Description("Filter to quotes whose author field contains this value (case-insensitive).")] string? author = null,
        [Description("Filter to quotes whose source field contains this value (case-insensitive).")] string? source = null,
        [Description("Return only quotes from this year or later (inclusive).")] string? yearFrom = null,
        [Description("Return only quotes from this year or earlier (inclusive).")] string? yearTo = null,
        [Description("Shorthand for yearFrom=N&yearTo=N — matches quotes from exactly this year.")] string? year = null,
        [Description("Shorthand for yearFrom=N&yearTo=N+9 — e.g. `1980` matches 1980–1989. Must be divisible by 10.")] string? decade = null)
    {
        logger.LogInformation("[Api - Random] n={N} type={Type} genre={Genre} lang={Lang}", n, type, genre, lang);

        if (ValidateCommon(localizer, lang) is { } err) return err;

        var count = 1;
        if (n is not null && (!int.TryParse(n, out count) || count < 1 || count > 100))
            return Results.Problem(
                detail: localizer[ApiMessages.RandomNOutOfRange],
                statusCode: StatusCodes.Status400BadRequest);

        if (!TryParseYear(yearFrom, out var yf)) return YearParseError(localizer, "yearFrom");
        if (!TryParseYear(yearTo,   out var yt)) return YearParseError(localizer, "yearTo");
        if (!TryParseYear(year,     out var yr)) return YearParseError(localizer, "year");
        if (!TryParseYear(decade,   out var dc)) return YearParseError(localizer, "decade");

        if (dc is not null)
        {
            if (dc % 10 != 0)
                return Results.Json(
                    FilterEnvelope(FilteredResultStatus.InvalidInput, localizer[ApiMessages.DecadeInvalid]),
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            yf = dc;
            yt = dc + 9;
        }
        else if (yr is not null)
        {
            yf = yr;
            yt = yr;
        }

        if (yf is not null && yt is not null && yf > yt)
            return Results.Json(
                FilterEnvelope(FilteredResultStatus.InvalidInput, localizer[ApiMessages.YearRangeInvalid]),
                statusCode: StatusCodes.Status422UnprocessableEntity);

        if (ValidateFilterParams(localizer, type, genre, character, author, source) is { } invalid)
            return ToValidationResult(invalid);

        var result = service.GetRandom(count, type, genre, character, author, source, lang, yf, yt);

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
        ILogger<Log> logger,
        [Description("ISO 639-1 language code (e.g. `nl`, `de`). Falls back to the original language when no translation exists."), DefaultValue("en")] string? lang = null)
    {
        logger.LogInformation("[Api - GetById] id={Id} lang={Lang}", id, lang);

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
        ILogger<Log> logger,
        [Description("Search term. Matched case-insensitively against the selected field (or all fields when `field` is omitted).")] string? q = null,
        [Description("Maximum number of results to return (1–100)."), DefaultValue(20)] string? limit = null,
        [Description("Filter by type (repeatable). One of: `movie`, `tv`, `anime`, `book`, `person`. Multiple values use OR logic.")] string[]? type = null,
        [Description("Filter by genre tag (repeatable, e.g. `sci-fi`, `drama`). Multiple values use OR logic.")] string[]? genre = null,
        [Description("ISO 639-1 language code (e.g. `nl`, `de`). Falls back to the original language when no translation exists."), DefaultValue("en")] string? lang = null,
        [Description("Restrict search to a specific field. One of: `quote`, `source`, `character`, `author`. Omit to search all fields.")] string? field = null,
        [Description("Return only quotes from this year or later (inclusive).")] string? yearFrom = null,
        [Description("Return only quotes from this year or earlier (inclusive).")] string? yearTo = null,
        [Description("Shorthand for yearFrom=N&yearTo=N — matches quotes from exactly this year.")] string? year = null,
        [Description("Shorthand for yearFrom=N&yearTo=N+9 — e.g. `1980` matches 1980–1989. Must be divisible by 10.")] string? decade = null)
    {
        logger.LogInformation("[Api - Search] q={Q} field={Field} limit={Limit} type={Type} lang={Lang}", q, field, limit, type, lang);

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

        if (!TryParseYear(yearFrom, out var yf)) return YearParseError(localizer, "yearFrom");
        if (!TryParseYear(yearTo,   out var yt)) return YearParseError(localizer, "yearTo");
        if (!TryParseYear(year,     out var yr)) return YearParseError(localizer, "year");
        if (!TryParseYear(decade,   out var dc)) return YearParseError(localizer, "decade");

        if (ValidateFilterParams(localizer, type, genre, null, null, null) is { } invalid)
            return ToValidationResult(invalid);

        if (dc is not null)
        {
            if (dc % 10 != 0)
                return Results.Problem(detail: localizer[ApiMessages.DecadeInvalid], statusCode: StatusCodes.Status422UnprocessableEntity);
            yf = dc;
            yt = dc + 9;
        }
        else if (yr is not null)
        {
            yf = yr;
            yt = yr;
        }

        if (yf is not null && yt is not null && yf > yt)
            return Results.Problem(detail: localizer[ApiMessages.YearRangeInvalid], statusCode: StatusCodes.Status422UnprocessableEntity);

        var result = service.Search(q, limitValue, type, genre, lang, field?.ToLowerInvariant(), yf, yt);

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

    private static IResult GetAll(
        IQuoteService service,
        IApiLocalizer localizer,
        ILogger<Log> logger,
        [Description("Page number, 1-based."), DefaultValue(1)] string? page = null,
        [Description("Number of quotes per page (1–100)."), DefaultValue(20)] string? pageSize = null,
        [Description("Filter by type (repeatable). One of: `movie`, `tv`, `anime`, `book`, `person`. Multiple values use OR logic.")] string[]? type = null,
        [Description("Filter by genre tag (repeatable, e.g. `sci-fi`, `drama`). Multiple values use OR logic.")] string[]? genre = null,
        [Description("ISO 639-1 language code (e.g. `nl`, `de`). Falls back to the original language when no translation exists."), DefaultValue("en")] string? lang = null,
        [Description("Return only quotes from this year or later (inclusive).")] string? yearFrom = null,
        [Description("Return only quotes from this year or earlier (inclusive).")] string? yearTo = null,
        [Description("Shorthand for yearFrom=N&yearTo=N — matches quotes from exactly this year.")] string? year = null,
        [Description("Shorthand for yearFrom=N&yearTo=N+9 — e.g. `1980` matches 1980–1989. Must be divisible by 10.")] string? decade = null)
    {
        logger.LogInformation("[Api - GetAll] page={Page} pageSize={PageSize} type={Type} lang={Lang}", page, pageSize, type, lang);

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

        if (!TryParseYear(yearFrom, out var yf)) return YearParseError(localizer, "yearFrom");
        if (!TryParseYear(yearTo,   out var yt)) return YearParseError(localizer, "yearTo");
        if (!TryParseYear(year,     out var yr)) return YearParseError(localizer, "year");
        if (!TryParseYear(decade,   out var dc)) return YearParseError(localizer, "decade");

        if (ValidateFilterParams(localizer, type, genre, null, null, null) is { } invalid)
            return ToValidationResult(invalid);

        if (dc is not null)
        {
            if (dc % 10 != 0)
                return Results.Problem(detail: localizer[ApiMessages.DecadeInvalid], statusCode: StatusCodes.Status422UnprocessableEntity);
            yf = dc;
            yt = dc + 9;
        }
        else if (yr is not null)
        {
            yf = yr;
            yt = yr;
        }

        if (yf is not null && yt is not null && yf > yt)
            return Results.Problem(detail: localizer[ApiMessages.YearRangeInvalid], statusCode: StatusCodes.Status422UnprocessableEntity);

        return Results.Ok(service.GetAll(pageValue, pageSizeValue, type, genre, lang, yf, yt));
    }
}

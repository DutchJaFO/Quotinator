using Dapper;
using Quotinator.Core.Data.Enums;
using Quotinator.Core.Helpers;
using Quotinator.Core.Models;
using Quotinator.Core.Services;
using Quotinator.Data.Connections;
using Quotinator.Data.Models;
using GenreEnum = Quotinator.Core.Data.Enums.Genre;

namespace Quotinator.Core.Data;

/// <summary>
/// <see cref="IQuoteService"/> implementation backed by SQLite + Dapper.
/// All queries use parameterised SQL — never string-concatenated user input.
/// </summary>
public sealed class SqliteQuoteService : IQuoteService
{
    private readonly IDbConnectionFactory _factory;

    // Maps DB enum name back to the API genre tag for response serialisation.
    private static readonly IReadOnlyDictionary<string, string> GenreDbToApi =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Action"]     = "action",
            ["Adventure"]  = "adventure",
            ["Animation"]  = "animation",
            ["Comedy"]     = "comedy",
            ["Drama"]      = "drama",
            ["Fantasy"]    = "fantasy",
            ["Fiction"]    = "fiction",
            ["Horror"]     = "horror",
            ["Mystery"]    = "mystery",
            ["NonFiction"] = "non-fiction",
            ["Romance"]    = "romance",
            ["SciFi"]      = "sci-fi",
            ["Thriller"]   = "thriller",
        };

    /// <summary>Initialises the service with the connection factory used for all database queries.</summary>
    /// <param name="factory">Factory used to open SQLite connections.</param>
    public SqliteQuoteService(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    // -------------------------------------------------------------------------
    #region IQuoteService

    /// <inheritdoc/>
    public QuoteResponse? GetById(string id, string? lang = null)
    {
        using var connection = _factory.CreateConnection();
        connection.Open();

        var row = connection.QueryFirstOrDefault<QuoteRow>(Sql.Quotes.SelectById(),
            new { id, lang = TranslationLang(lang, null) });

        if (row is null) return null;

        var genres = LoadGenres(connection, id);
        return ToResponse(row, genres, lang);
    }

    /// <inheritdoc/>
    public FilteredQuoteResult<QuoteResponse> GetRandom(
        int count,
        string[]? types = null,
        string[]? genres = null,
        string? character = null,
        string? author = null,
        string? source = null,
        string? lang = null,
        int? yearFrom = null,
        int? yearTo = null)
    {
        using var connection = _factory.CreateConnection();
        connection.Open();

        var (whereClause, filterParams) = BuildFilterWhere(types, genres, lang, character, author, source, yearFrom, yearTo);

        var totalMatching = connection.ExecuteScalar<int>(
            Sql.Quotes.CountRandom(whereClause),
            filterParams);

        var rp = new DynamicParameters(filterParams);
        rp.Add("count", count);
        var rows = connection.Query<QuoteRow>(
            Sql.Quotes.SelectRandom(whereClause), rp).ToList();

        var items = rows.Select(r =>
        {
            var translationLang = TranslationLang(lang, r.OriginalLanguage);
            if (translationLang is not null)
            {
                var translated = connection.QueryFirstOrDefault<QuoteRow>(
                    Sql.Quotes.SelectById(),
                    new { id = r.Id, lang = translationLang });
                if (translated is not null) r = translated;
            }
            return ToResponse(r, LoadGenres(connection, r.Id), lang);
        }).ToList();

        return new FilteredQuoteResult<QuoteResponse>
        {
            Status        = items.Count > 0 ? FilteredResultStatus.Ok : FilteredResultStatus.NoResults,
            Items         = items,
            TotalMatching = totalMatching,
        };
    }

    /// <inheritdoc/>
    public PagedResult<QuoteResponse> GetAll(int page, int pageSize, string[]? types = null, string[]? genres = null, string? lang = null, int? yearFrom = null, int? yearTo = null)
    {
        using var connection = _factory.CreateConnection();
        connection.Open();

        var (whereClause, parameters) = BuildFilterWhere(types, genres, lang, yearFrom, yearTo);

        var total = connection.ExecuteScalar<int>(
            Sql.Quotes.CountGetAll(whereClause),
            parameters);

        var offset = (page - 1) * pageSize;
        var p = new DynamicParameters(parameters);
        p.Add("pageSize", pageSize);
        p.Add("offset",   offset);
        var rows = connection.Query<QuoteRow>(
            Sql.Quotes.SelectPaged(whereClause), p).ToList();

        var items = rows.Select(r => ToResponse(r, LoadGenres(connection, r.Id), lang)).ToList();
        return new PagedResult<QuoteResponse>(items, page, pageSize, total);
    }

    /// <inheritdoc/>
    public IReadOnlyList<QuoteResponse> Search(string query, int limit, string[]? types = null, string[]? genres = null, string? lang = null, string? field = null, int? yearFrom = null, int? yearTo = null)
    {
        using var connection = _factory.CreateConnection();
        connection.Open();

        var like = $"%{query}%";

        var fieldFilter = field switch
        {
            "quote"     => Sql.SearchField.Quote,
            "source"    => Sql.SearchField.Source,
            "character" => Sql.SearchField.Character,
            "author"    => Sql.SearchField.Author,
            _           => Sql.SearchField.All
        };

        var (typeGenreWhere, filterParams) = BuildFilterWhere(types, genres, lang, yearFrom, yearTo);

        var sql = Sql.Quotes.SelectSearch(typeGenreWhere, fieldFilter);

        var p = new DynamicParameters(filterParams);
        p.Add("like",  like);
        p.Add("limit", limit);

        var rows = connection.Query<QuoteRow>(sql, p).ToList();
        return rows.Select(r => ToResponse(r, LoadGenres(connection, r.Id), lang)).ToList();
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Helpers

    private static string? TranslationLang(string? lang, string? originalLanguage)
    {
        if (lang is null) return null;
        if (originalLanguage is not null
            && lang.Equals(originalLanguage, StringComparison.OrdinalIgnoreCase))
            return null;
        return lang;
    }

    private static IReadOnlyList<SafeValue<GenreEnum?>> LoadGenres(System.Data.IDbConnection connection, string quoteId)
        => connection.Query<SafeValue<GenreEnum?>>(
            Sql.QuoteGenres.LoadForQuote,
            new { id = quoteId }).ToList();

    private static QuoteResponse ToResponse(QuoteRow row, IReadOnlyList<SafeValue<GenreEnum?>> genres, string? requestedLang)
    {
        var effectiveLang = string.IsNullOrEmpty(row.EffectiveLanguage)
            ? row.OriginalLanguage
            : row.EffectiveLanguage;

        return new QuoteResponse
        {
            Id               = row.Id,
            Quote            = row.QuoteText,
            Language         = effectiveLang,
            OriginalLanguage = row.OriginalLanguage,
            Source           = row.Source,
            Date             = row.Date,
            Character        = row.Character,
            Author           = row.Author,
            Type             = row.SourceType?.Parsed?.ToString().ToLowerInvariant()
                               ?? row.SourceType?.Raw.ToLowerInvariant()
                               ?? string.Empty,
            Genres           = genres
                .Select(g =>
                {
                    var enumName = g.Parsed?.ToString() ?? g.Raw;
                    return GenreDbToApi.TryGetValue(enumName, out var api) ? api : enumName.ToLowerInvariant();
                })
                .Where(g => !string.IsNullOrEmpty(g))
                .ToList()
        };
    }

    // Overload without text filters — used by GetAll and Search.
    internal static (string Sql, object Parameters) BuildFilterWhere(string[]? types, string[]? genres, string? lang, int? yearFrom = null, int? yearTo = null)
        => BuildFilterWhere(types, genres, lang, null, null, null, yearFrom, yearTo);

    internal static (string Sql, DynamicParameters Parameters) BuildFilterWhere(
        string[]? types, string[]? genres, string? lang,
        string? character, string? author, string? source,
        int? yearFrom = null, int? yearTo = null)
    {
        var dbTypes  = types  is { Length: > 0 } ? types.Select(NormaliseType).ToArray()  : null;
        var dbGenres = genres is { Length: > 0 } ? genres.Select(NormaliseGenre).ToArray() : null;

        var clauses = new List<string> { "q.IsDeleted = 0", "s.IsDeleted = 0" };
        if (dbTypes  is not null) clauses.Add("s.Type IN @dbTypes");
        if (dbGenres is not null) clauses.Add("EXISTS (SELECT 1 FROM QuoteGenres qg WHERE qg.QuoteId = q.Id AND qg.Genre IN @dbGenres AND qg.IsDeleted = 0)");
        if (character is not null) clauses.Add("c.Name LIKE @characterLike");
        if (author    is not null) clauses.Add("p.Name LIKE @authorLike");
        if (source    is not null) clauses.Add("s.Title LIKE @sourceLike");
        if (yearFrom  is not null) clauses.Add("CAST(SUBSTR(s.Date, 1, 4) AS INTEGER) >= @yearFrom");
        if (yearTo    is not null) clauses.Add("CAST(SUBSTR(s.Date, 1, 4) AS INTEGER) <= @yearTo");

        var p = new DynamicParameters();
        p.Add("lang", (string?)null);
        if (dbTypes  is not null) p.Add("dbTypes",  dbTypes);
        if (dbGenres is not null) p.Add("dbGenres", dbGenres);
        if (character is not null) p.Add("characterLike", $"%{character}%");
        if (author    is not null) p.Add("authorLike",    $"%{author}%");
        if (source    is not null) p.Add("sourceLike",    $"%{source}%");
        if (yearFrom  is not null) p.Add("yearFrom", yearFrom);
        if (yearTo    is not null) p.Add("yearTo",   yearTo);

        return ("WHERE " + string.Join(" AND ", clauses), p);
    }

    private static string NormaliseType(string raw)
        => Enum.TryParse<QuoteType>(raw, ignoreCase: true, out var t) ? t.ToString() : raw;

    private static string NormaliseGenre(string raw)
        => InputValidation.GenreApiToDb.TryGetValue(raw, out var db) ? db : raw;

    #endregion

    // -------------------------------------------------------------------------
    #region Private row DTO

    private sealed class QuoteRow
    {
        public string              Id                { get; init; } = string.Empty;
        public string              QuoteText         { get; init; } = string.Empty;
        public string              OriginalLanguage  { get; init; } = "en";
        public string              Source            { get; init; } = string.Empty;
        public string?             Date              { get; init; }
        public SafeValue<QuoteType?> SourceType      { get; init; } = SafeValue<QuoteType?>.Empty;
        public string?             Character         { get; init; }
        public string?             Author            { get; init; }
        public string              EffectiveLanguage { get; init; } = string.Empty;
    }

    #endregion
}

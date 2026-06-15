using Dapper;
using Quotinator.Core.Data.Enums;
using Quotinator.Core.Helpers;
using Quotinator.Core.Models;
using Quotinator.Core.Services;
using Quotinator.Data.Data;
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

        var row = connection.QueryFirstOrDefault<QuoteRow>(QuoteSelectSql + " WHERE q.Id = @id AND q.IsDeleted = 0",
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
            $"SELECT COUNT(*) FROM Quotes q JOIN Sources s ON s.Id = q.SourceId AND s.IsDeleted = 0 LEFT JOIN Characters c ON c.Id = q.CharacterId AND c.IsDeleted = 0 LEFT JOIN People p ON p.Id = q.PersonId AND p.IsDeleted = 0 {whereClause}",
            filterParams);

        var rp = new DynamicParameters(filterParams);
        rp.Add("count", count);
        var rows = connection.Query<QuoteRow>(
            QuoteSelectSql + $" {whereClause} ORDER BY RANDOM() LIMIT @count", rp).ToList();

        var items = rows.Select(r =>
        {
            var translationLang = TranslationLang(lang, r.OriginalLanguage);
            if (translationLang is not null)
            {
                var translated = connection.QueryFirstOrDefault<QuoteRow>(
                    QuoteSelectSql + " WHERE q.Id = @id AND q.IsDeleted = 0",
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
            $"SELECT COUNT(*) FROM Quotes q JOIN Sources s ON s.Id = q.SourceId AND s.IsDeleted = 0 {whereClause}",
            parameters);

        var offset = (page - 1) * pageSize;
        var p = new DynamicParameters(parameters);
        p.Add("pageSize", pageSize);
        p.Add("offset",   offset);
        var rows = connection.Query<QuoteRow>(
            QuoteSelectSql + $" {whereClause} ORDER BY q.Id LIMIT @pageSize OFFSET @offset", p).ToList();

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
            "quote"     => "q.QuoteText LIKE @like",
            "source"    => "s.Title LIKE @like",
            "character" => "c.Name LIKE @like",
            "author"    => "p.Name LIKE @like",
            _           => "(q.QuoteText LIKE @like OR s.Title LIKE @like OR c.Name LIKE @like OR p.Name LIKE @like)"
        };

        var (typeGenreWhere, filterParams) = BuildFilterWhere(types, genres, lang, yearFrom, yearTo);

        var sql = QuoteSelectSql
            + $" {typeGenreWhere}"
            + $" AND {fieldFilter}"
            + " LIMIT @limit";

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
            "SELECT Genre FROM QuoteGenres WHERE QuoteId = @id AND IsDeleted = 0",
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
    private static (string Sql, object Parameters) BuildFilterWhere(string[]? types, string[]? genres, string? lang, int? yearFrom = null, int? yearTo = null)
        => BuildFilterWhere(types, genres, lang, null, null, null, yearFrom, yearTo);

    private static (string Sql, DynamicParameters Parameters) BuildFilterWhere(
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
    #region SQL

    // The @lang parameter is substituted into the LEFT JOINs so COALESCE picks up
    // translations when available, falling back to the original value silently.
    private const string QuoteSelectSql = """
        SELECT
            q.Id,
            COALESCE(qt.QuoteText,  q.QuoteText)  AS QuoteText,
            q.OriginalLanguage,
            COALESCE(st.Title,      s.Title)       AS Source,
            s.Date,
            s.Type                                 AS SourceType,
            COALESCE(ct.Name,       c.Name)        AS Character,
            p.Name                                 AS Author,
            CASE WHEN qt.QuoteText IS NOT NULL THEN @lang ELSE q.OriginalLanguage END AS EffectiveLanguage
        FROM   Quotes          q
        JOIN   Sources         s  ON  s.Id  = q.SourceId                                          AND s.IsDeleted  = 0
        LEFT JOIN Characters   c  ON  c.Id  = q.CharacterId                                       AND c.IsDeleted  = 0
        LEFT JOIN People       p  ON  p.Id  = q.PersonId                                          AND p.IsDeleted  = 0
        LEFT JOIN QuoteTranslations    qt ON qt.QuoteId     = q.Id AND qt.Language = @lang        AND qt.IsDeleted = 0
        LEFT JOIN SourceTranslations   st ON st.SourceId    = s.Id AND st.Language = @lang        AND st.IsDeleted = 0
        LEFT JOIN CharacterTranslations ct ON ct.CharacterId = c.Id AND ct.Language = @lang       AND ct.IsDeleted = 0
        """;

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

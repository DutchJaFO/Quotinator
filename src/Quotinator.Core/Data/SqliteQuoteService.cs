using Dapper;
using Quotinator.Core.Data.Enums;
using Quotinator.Core.Models;
using Quotinator.Core.Services;
using GenreEnum = Quotinator.Core.Data.Enums.Genre;

namespace Quotinator.Core.Data;

/// <summary>
/// <see cref="IQuoteService"/> implementation backed by SQLite + Dapper.
/// All queries use parameterised SQL — never string-concatenated user input.
/// </summary>
public sealed class SqliteQuoteService : IQuoteService
{
    private readonly IDbConnectionFactory _factory;

    public SqliteQuoteService(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    // -------------------------------------------------------------------------
    #region IQuoteService

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

    public IReadOnlyList<QuoteResponse> GetRandom(int count, string? lang = null)
    {
        using var connection = _factory.CreateConnection();
        connection.Open();

        var rows = connection.Query<QuoteRow>(
            QuoteSelectSql + " WHERE q.IsDeleted = 0 ORDER BY RANDOM() LIMIT @count",
            new { count, lang = (string?)null }).ToList();

        return rows.Select(r =>
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
    }

    public PagedResult<QuoteResponse> GetAll(int page, int pageSize, string? type = null, string? genre = null, string? lang = null)
    {
        using var connection = _factory.CreateConnection();
        connection.Open();

        var (whereClause, parameters) = BuildFilterWhere(type, genre, lang);

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

    public IReadOnlyList<QuoteResponse> Search(string query, int limit, string? type = null, string? genre = null, string? lang = null, string? field = null)
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

        var (typeGenreWhere, filterParams) = BuildFilterWhere(type, genre, lang);

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
                .Select(g => g.Parsed?.ToString().ToLowerInvariant() ?? g.Raw.ToLowerInvariant())
                .Where(g => !string.IsNullOrEmpty(g))
                .ToList()
        };
    }

    private static (string Sql, object Parameters) BuildFilterWhere(string? type, string? genre, string? lang)
    {
        var typeStr  = type  is not null ? NormaliseType(type)  : null;
        var genreStr = genre is not null ? NormaliseGenre(genre) : null;

        var clauses = new List<string> { "q.IsDeleted = 0", "s.IsDeleted = 0" };
        if (typeStr  is not null) clauses.Add("s.Type = @typeStr");
        if (genreStr is not null) clauses.Add("EXISTS (SELECT 1 FROM QuoteGenres qg WHERE qg.QuoteId = q.Id AND qg.Genre = @genreStr AND qg.IsDeleted = 0)");

        return ("WHERE " + string.Join(" AND ", clauses),
                new { lang = (string?)null, typeStr, genreStr });
    }

    private static string NormaliseType(string raw)
        => Enum.TryParse<QuoteType>(raw, ignoreCase: true, out var t) ? t.ToString() : raw;

    private static string NormaliseGenre(string raw)
        => Enum.TryParse<GenreEnum>(raw, ignoreCase: true, out var g) ? g.ToString() : raw;

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

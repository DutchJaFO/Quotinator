using System.Text.Json;
using Dapper;
using Quotinator.Core.Helpers;
using Quotinator.Core.Models;
using Quotinator.Core.Services;
using Quotinator.Data.Connections;
using Quotinator.Data.Models;
using Quotinator.Engine.Queries;

namespace Quotinator.Engine.Services;

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
        return ToResponse(row, genres, lang, LoadConversationMemberships(connection, id));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// #69: conversation-aware deduplication. When a picked quote belongs to one or more
    /// conversations, one is chosen at random, every quote id its lines reference is added to a
    /// running exclusion set (not just the quote that triggered the selection), and that
    /// conversation's full detail is embedded on the returned <see cref="QuoteResponse.EmbeddedConversation"/>.
    /// Re-queries in a loop (excluding everything picked/excluded so far) until <paramref name="count"/>
    /// distinct quotes are gathered or the pool is exhausted — a single <c>ORDER BY RANDOM() LIMIT</c>
    /// can't express "exclude a growing set discovered mid-selection" in one round-trip.
    /// </remarks>
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

        var items       = new List<QuoteResponse>();
        var excludedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Safety valve: a pathological case (e.g. every remaining quote in the pool belongs to one
        // giant shared conversation) could otherwise loop many times for a small net gain each pass.
        // totalMatching + 1 is always enough passes to either fill count or prove the pool exhausted.
        var maxPasses = totalMatching + 1;
        for (var pass = 0; pass < maxPasses && items.Count < count; pass++)
        {
            var remaining     = count - items.Count;
            var effectiveWhere = excludedIds.Count > 0 ? whereClause + " AND q.Id NOT IN @excludedIds" : whereClause;

            var rp = new DynamicParameters(filterParams);
            rp.Add("count", remaining);
            if (excludedIds.Count > 0) rp.Add("excludedIds", excludedIds);

            var rows = connection.Query<QuoteRow>(Sql.Quotes.SelectRandom(effectiveWhere), rp).ToList();
            if (rows.Count == 0) break; // pool exhausted — nothing left outside the exclusion set

            foreach (var row in rows)
            {
                if (items.Count >= count) break;
                if (!excludedIds.Add(row.Id)) continue; // already covered by a conversation picked earlier this call

                var r = row;
                var translationLang = TranslationLang(lang, r.OriginalLanguage);
                if (translationLang is not null)
                {
                    var translated = connection.QueryFirstOrDefault<QuoteRow>(Sql.Quotes.SelectById(), new { id = r.Id, lang = translationLang });
                    if (translated is not null) r = translated;
                }

                var memberships = LoadConversationMemberships(connection, r.Id);
                ConversationResponse? embedded = null;
                if (memberships.Count > 0)
                {
                    var chosen = memberships[Random.Shared.Next(memberships.Count)];
                    foreach (var quoteId in connection.Query<string>(Sql.ConversationLines.SelectQuoteIdsForConversation, new { conversationId = chosen.ConversationId }))
                        excludedIds.Add(quoteId);
                    embedded = BuildConversationResponse(connection, chosen.ConversationId, lang);
                }

                items.Add(ToResponse(r, LoadGenres(connection, r.Id), lang, memberships, embedded));
            }
        }

        return new FilteredQuoteResult<QuoteResponse>
        {
            Status         = items.Count > 0 ? FilteredResultStatus.Ok : FilteredResultStatus.NoResults,
            Items          = items,
            TotalMatching  = totalMatching,
            RequestedCount = count,
            ReturnedCount  = items.Count,
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

        var items = rows.Select(r => ToResponse(r, LoadGenres(connection, r.Id), lang, LoadConversationMemberships(connection, r.Id))).ToList();
        return new PagedResult<QuoteResponse>(items, page, pageSize, total);
    }

    /// <inheritdoc/>
    public FilteredQuoteResult<QuoteResponse> Search(string query, int limit, string[]? types = null, string[]? genres = null, string? lang = null, string? field = null, int? yearFrom = null, int? yearTo = null)
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
        var items = rows.Select(r => ToResponse(r, LoadGenres(connection, r.Id), lang, LoadConversationMemberships(connection, r.Id))).ToList();

        return new FilteredQuoteResult<QuoteResponse>
        {
            Status        = items.Count > 0 ? FilteredResultStatus.Ok : FilteredResultStatus.NoResults,
            Items         = items,
            TotalMatching = items.Count,
        };
    }

    /// <inheritdoc/>
    public ConversationResponse? GetConversation(string id, string? lang = null)
    {
        using var connection = _factory.CreateConnection();
        connection.Open();

        return BuildConversationResponse(connection, id, lang);
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

    private static IReadOnlyList<SafeValue<Genre?>> LoadGenres(System.Data.IDbConnection connection, string quoteId)
        => connection.Query<SafeValue<Genre?>>(
            Sql.QuoteGenres.LoadForQuote,
            new { id = quoteId }).ToList();

    private static QuoteResponse ToResponse(
        QuoteRow row, IReadOnlyList<SafeValue<Genre?>> genres, string? requestedLang,
        IReadOnlyList<QuoteConversationMembership>? conversations = null,
        ConversationResponse? embeddedConversation = null)
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
                .ToList(),
            Conversations        = conversations is { Count: > 0 } ? conversations : null,
            EmbeddedConversation = embeddedConversation,
        };
    }

    /// <summary>Every conversation <paramref name="quoteId"/> appears in — backs <see cref="QuoteResponse.Conversations"/> on every read call, and <c>/random</c>'s conversation-selection step.</summary>
    private static IReadOnlyList<QuoteConversationMembership> LoadConversationMemberships(System.Data.IDbConnection connection, string quoteId)
        => connection.Query<QuoteConversationMembership>(
            Sql.ConversationLines.SelectMembershipForQuote,
            new { quoteId }).ToList();

    /// <summary>
    /// Builds the full ordered line list for a conversation — shared by <see cref="GetConversation"/>
    /// (id from a user-supplied route parameter) and <see cref="GetRandom"/>'s embed (id already known
    /// to exist, from a membership row just read). Both cases go through the same case-insensitive
    /// lookup; it's a safe no-op superset for the already-correctly-cased internal case. Embedded
    /// quote lines never carry their own <see cref="QuoteResponse.Conversations"/> or
    /// <see cref="QuoteResponse.EmbeddedConversation"/> — no recursive expansion.
    /// </summary>
    private static ConversationResponse? BuildConversationResponse(System.Data.IDbConnection connection, string conversationId, string? lang)
    {
        var conversation = connection.QueryFirstOrDefault<ConversationRow>(Sql.Conversations.SelectForRead, new { id = conversationId });
        if (conversation is null) return null;

        var lineRows = connection.Query<ConversationLineRow>(Sql.ConversationLines.SelectByConversationId, new { conversationId = conversation.Id }).ToList();

        var lines = lineRows.Select(lr => BuildLineResponse(connection, lr, lang)).ToList();

        return new ConversationResponse
        {
            Id          = conversation.Id,
            Description = conversation.Description,
            Lines       = lines,
        };
    }

    private static ConversationLineResponse BuildLineResponse(System.Data.IDbConnection connection, ConversationLineRow lineRow, string? lang)
    {
        var wireType = ConversationLineTypeWire(lineRow.LineType);

        switch (wireType)
        {
            case "quote":
            {
                var quoteRow = connection.QueryFirstOrDefault<QuoteRow>(Sql.Quotes.SelectById(), new { id = lineRow.QuoteId, lang = TranslationLang(lang, null) });
                var effectiveRow = quoteRow;
                if (effectiveRow is not null)
                {
                    var translationLang = TranslationLang(lang, effectiveRow.OriginalLanguage);
                    if (translationLang is not null)
                    {
                        var translated = connection.QueryFirstOrDefault<QuoteRow>(Sql.Quotes.SelectById(), new { id = lineRow.QuoteId, lang = translationLang });
                        if (translated is not null) effectiveRow = translated;
                    }
                }
                var quote = effectiveRow is null ? null : ToResponse(effectiveRow, LoadGenres(connection, effectiveRow.Id), lang);
                return new ConversationLineResponse { Order = lineRow.Order, Type = wireType, Quote = quote };
            }
            case "stage_direction":
            {
                var sd = connection.QueryFirstOrDefault<TextEntityRow>(Sql.StageDirections.SelectByIdWithTranslation, new { id = lineRow.StageDirectionId, lang = lang ?? "en" });
                return new ConversationLineResponse
                {
                    Order        = lineRow.Order,
                    Type         = wireType,
                    Text         = sd?.Text,
                    ImageUrl     = sd?.ImageUrl,
                    Language     = sd?.EffectiveLanguage,
                    IsTranslated = sd is not null && sd.EffectiveLanguage != "en",
                };
            }
            default: // "sound_cue"
            {
                var sc = connection.QueryFirstOrDefault<TextEntityRow>(Sql.SoundCues.SelectByIdWithTranslation, new { id = lineRow.SoundCueId, lang = lang ?? "en" });
                return new ConversationLineResponse
                {
                    Order        = lineRow.Order,
                    Type         = wireType,
                    Text         = sc?.Text,
                    SoundFileUrl = sc?.SoundFileUrl,
                    ImageUrl     = sc?.ImageUrl,
                    Language     = sc?.EffectiveLanguage,
                    IsTranslated = sc is not null && sc.EffectiveLanguage != "en",
                };
            }
        }
    }

    // Reuses the exact naming policy ConversationLineTypeJsonConverter applies on the import side
    // (JsonNamingPolicy.SnakeCaseLower) instead of a second, hand-maintained string mapping that
    // could silently drift from it — same pattern as SqliteQuoteImportService.ToWireString.
    private static string ConversationLineTypeWire(string dbLineType) => JsonNamingPolicy.SnakeCaseLower.ConvertName(dbLineType);

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

    private sealed class ConversationRow
    {
        public string  Id          { get; init; } = string.Empty;
        public string? Description { get; init; }
    }

    private sealed class ConversationLineRow
    {
        public int     Order            { get; init; }
        public string  LineType         { get; init; } = string.Empty;
        public string? QuoteId          { get; init; }
        public string? StageDirectionId { get; init; }
        public string? SoundCueId       { get; init; }
    }

    // Shared row shape for StageDirections.SelectByIdWithTranslation / SoundCues.SelectByIdWithTranslation —
    // SoundFileUrl is simply unpopulated (default null) when mapping a StageDirections row.
    private sealed class TextEntityRow
    {
        public string  Id                { get; init; } = string.Empty;
        public string  Text              { get; init; } = string.Empty;
        public string? SoundFileUrl      { get; init; }
        public string? ImageUrl          { get; init; }
        public string  EffectiveLanguage { get; init; } = "en";
    }

    #endregion
}

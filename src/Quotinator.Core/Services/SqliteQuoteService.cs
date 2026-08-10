using Quotinator.Core.Enums;
using System.Text.Json;
using Dapper;
using Quotinator.Core.Helpers;
using Quotinator.Core.Models;
using Quotinator.Core.Services;
using Quotinator.Data.Connections;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Core.Queries;
using IdClauses = Quotinator.Data.Queries.IdClauses;

namespace Quotinator.Core.Services;

/// <summary>
/// <see cref="IQuoteService"/> implementation backed by SQLite + Dapper.
/// All queries use parameterised SQL — never string-concatenated user input.
/// </summary>
/// <remarks>Initialises the service with the connection factory and the join-query repositories used
/// by a Conversation's per-line lookups (ADR 017) — all other queries use the connection factory
/// directly.</remarks>
/// <param name="factory">Factory used to open SQLite connections.</param>
/// <param name="unicodeAwareSearch">
/// Whether <c>LIKE</c>-based fuzzy matching (quote search, character/author/source filters) uses
/// the Unicode-aware <c>UNICODE_CONTAINS</c> function instead of SQLite's own ASCII-only
/// <c>LIKE</c>. Opt-in, off by default — see issue #222.
/// </param>
/// <param name="quoteLineRepository">Executes a Conversation quote-line's translation-resolved lookup.</param>
/// <param name="stageDirectionLineRepository">Executes a Conversation stage-direction-line's translation-resolved lookup.</param>
/// <param name="soundCueLineRepository">Executes a Conversation sound-cue-line's translation-resolved lookup.</param>
public sealed class SqliteQuoteService(
    IDbConnectionFactory factory,
    bool unicodeAwareSearch,
    JoinQueryRepository<QuoteRow> quoteLineRepository,
    JoinQueryRepository<StageDirectionLineRow> stageDirectionLineRepository,
    JoinQueryRepository<SoundCueLineRow> soundCueLineRepository) : IQuoteService
{
    private readonly IDbConnectionFactory _factory = factory;
    private readonly bool _unicodeAwareSearch = unicodeAwareSearch;
    private readonly JoinQueryRepository<QuoteRow> _quoteLineRepository = quoteLineRepository;
    private readonly JoinQueryRepository<StageDirectionLineRow> _stageDirectionLineRepository = stageDirectionLineRepository;
    private readonly JoinQueryRepository<SoundCueLineRow> _soundCueLineRepository = soundCueLineRepository;

    // Maps DB enum name back to the API genre tag for response serialisation.
    private static readonly Dictionary<string, string> GenreDbToApi =
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

    // -------------------------------------------------------------------------
    #region IQuoteService

    /// <inheritdoc/>
    public async Task<QuoteResponse?> GetById(string id, string? lang = null)
    {
        using var connection = _factory.CreateConnection();
        connection.Open();

        var row = await connection.QueryFirstOrDefaultAsync<QuoteRow>(Sql.Quotes.SelectById(),
            new { id, lang = TranslationLang(lang, null) });

        if (row is null) return null;

        var genres = await LoadGenres(connection, id);
        return ToResponse(row, genres, await LoadConversationMemberships(connection, id));
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
    public async Task<FilteredQuoteResult<QuoteResponse>> GetRandom(
        int count,
        string[]? types = null,
        string[]? genres = null,
        string? character = null,
        string? author = null,
        string? source = null,
        string? lang = null,
        int? yearFrom = null,
        int? yearTo = null,
        Guid? seriesId = null,
        Guid? universeId = null)
    {
        using var connection = _factory.CreateConnection();
        connection.Open();

        var (whereClause, filterParams) = BuildFilterWhere(types, genres, lang, _unicodeAwareSearch, character, author, source, seriesId, universeId, yearFrom, yearTo);

        var totalMatching = await connection.ExecuteScalarAsync<int>(
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
            var effectiveWhere = excludedIds.Count > 0 ? whereClause + $" AND {IdClauses.NotIn("q.Id", "excludedIds")}" : whereClause;

            var rp = new DynamicParameters(filterParams);
            rp.Add("count", remaining);
            if (excludedIds.Count > 0) rp.Add("excludedIds", excludedIds);

            var rows = (await connection.QueryAsync<QuoteRow>(Sql.Quotes.SelectRandom(effectiveWhere), rp)).ToList();
            if (rows.Count == 0) break; // pool exhausted — nothing left outside the exclusion set

            foreach (var row in rows)
            {
                if (items.Count >= count) break;
                if (!excludedIds.Add(row.Id)) continue; // already covered by a conversation picked earlier this call

                var r = row;
                var translationLang = TranslationLang(lang, r.OriginalLanguage);
                if (translationLang is not null)
                {
                    var translated = await connection.QueryFirstOrDefaultAsync<QuoteRow>(Sql.Quotes.SelectById(), new { id = r.Id, lang = translationLang });
                    if (translated is not null) r = translated;
                }

                var memberships = await LoadConversationMemberships(connection, r.Id);
                ConversationResponse? embedded = null;
                if (memberships.Count > 0)
                {
                    var chosen = memberships[Random.Shared.Next(memberships.Count)];
                    foreach (var quoteId in await connection.QueryAsync<string>(Sql.ConversationLines.SelectQuoteIdsForConversation, new { conversationId = chosen.ConversationId }))
                        excludedIds.Add(quoteId);
                    embedded = await BuildConversationResponse(connection, chosen.ConversationId, lang);
                }

                items.Add(ToResponse(r, await LoadGenres(connection, r.Id), memberships, embedded));
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
    public async Task<PagedResult<QuoteResponse>> GetAll(int page, int pageSize, string[]? types = null, string[]? genres = null, string? lang = null, int? yearFrom = null, int? yearTo = null, Guid? seriesId = null, Guid? universeId = null)
    {
        using var connection = _factory.CreateConnection();
        connection.Open();

        var (whereClause, parameters) = BuildFilterWhere(types, genres, lang, _unicodeAwareSearch, seriesId, universeId, yearFrom, yearTo);

        var total = await connection.ExecuteScalarAsync<int>(
            Sql.Quotes.CountGetAll(whereClause),
            parameters);

        var limit  = pageSize == 0 ? -1 : pageSize;
        var offset = pageSize == 0 ? 0  : (page - 1) * pageSize;
        var p = new DynamicParameters(parameters);
        p.Add("pageSize", limit);
        p.Add("offset",   offset);
        var rows = (await connection.QueryAsync<QuoteRow>(
            Sql.Quotes.SelectPaged(whereClause), p)).ToList();

        var items = new List<QuoteResponse>(rows.Count);
        foreach (var r in rows)
            items.Add(ToResponse(r, await LoadGenres(connection, r.Id), await LoadConversationMemberships(connection, r.Id)));
        var effectivePageSize = pageSize == 0 ? items.Count : pageSize;
        return new PagedResult<QuoteResponse>(items, page, effectivePageSize, total);
    }

    /// <inheritdoc/>
    public async Task<FilteredQuoteResult<QuoteResponse>> Search(string query, int limit, string[]? types = null, string[]? genres = null, string? lang = null, string? field = null, int? yearFrom = null, int? yearTo = null, Guid? seriesId = null, Guid? universeId = null)
    {
        using var connection = _factory.CreateConnection();
        connection.Open();

        var like = _unicodeAwareSearch ? query : $"%{query}%";

        var fieldFilter = field switch
        {
            "quote"     => Sql.SearchField.Quote(_unicodeAwareSearch),
            "source"    => Sql.SearchField.Source(_unicodeAwareSearch),
            "character" => Sql.SearchField.Character(_unicodeAwareSearch),
            "author"    => Sql.SearchField.Author(_unicodeAwareSearch),
            _           => Sql.SearchField.All(_unicodeAwareSearch)
        };

        var (typeGenreWhere, filterParams) = BuildFilterWhere(types, genres, lang, _unicodeAwareSearch, seriesId, universeId, yearFrom, yearTo);

        var sql = Sql.Quotes.SelectSearch(typeGenreWhere, fieldFilter);

        var p = new DynamicParameters(filterParams);
        p.Add("like",  like);
        p.Add("limit", limit);

        var rows = (await connection.QueryAsync<QuoteRow>(sql, p)).ToList();
        var items = new List<QuoteResponse>(rows.Count);
        foreach (var r in rows)
            items.Add(ToResponse(r, await LoadGenres(connection, r.Id), await LoadConversationMemberships(connection, r.Id)));

        return new FilteredQuoteResult<QuoteResponse>
        {
            Status        = items.Count > 0 ? FilteredResultStatus.Ok : FilteredResultStatus.NoResults,
            Items         = items,
            TotalMatching = items.Count,
        };
    }

    /// <inheritdoc/>
    public async Task<ConversationResponse?> GetConversation(string id, string? lang = null)
    {
        using var connection = _factory.CreateConnection();
        connection.Open();

        return await BuildConversationResponse(connection, id, lang);
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

    private static async Task<IReadOnlyList<SafeValue<Genre?>>> LoadGenres(System.Data.IDbConnection connection, string quoteId)
        => [.. await connection.QueryAsync<SafeValue<Genre?>>(
            Sql.QuoteGenres.LoadForQuote,
            new { id = quoteId })];

    private static QuoteResponse ToResponse(
        QuoteRow row, IReadOnlyList<SafeValue<Genre?>> genres,
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
            Genres           = [.. genres
                .Select(g =>
                {
                    var enumName = g.Parsed?.ToString() ?? g.Raw;
                    return GenreDbToApi.TryGetValue(enumName, out var api) ? api : enumName.ToLowerInvariant();
                })
                .Where(g => !string.IsNullOrEmpty(g))],
            Series           = row.SeriesId   is { } sid ? new MasterDataReference(sid, row.SeriesName!)   : null,
            Universe         = row.UniverseId is { } uid ? new MasterDataReference(uid, row.UniverseName!) : null,
            Conversations        = conversations is { Count: > 0 } ? conversations : null,
            EmbeddedConversation = embeddedConversation,
        };
    }

    /// <summary>Every conversation <paramref name="quoteId"/> appears in — backs <see cref="QuoteResponse.Conversations"/> on every read call, and <c>/random</c>'s conversation-selection step.</summary>
    private static async Task<IReadOnlyList<QuoteConversationMembership>> LoadConversationMemberships(System.Data.IDbConnection connection, string quoteId)
        => [.. await connection.QueryAsync<QuoteConversationMembership>(
            Sql.ConversationLines.SelectMembershipForQuote,
            new { quoteId })];

    /// <summary>
    /// Builds the full ordered line list for a conversation — shared by <see cref="GetConversation"/>
    /// (id from a user-supplied route parameter) and <see cref="GetRandom"/>'s embed (id already known
    /// to exist, from a membership row just read). Both cases go through the same case-insensitive
    /// lookup; it's a safe no-op superset for the already-correctly-cased internal case. Embedded
    /// quote lines never carry their own <see cref="QuoteResponse.Conversations"/> or
    /// <see cref="QuoteResponse.EmbeddedConversation"/> — no recursive expansion.
    /// </summary>
    private async Task<ConversationResponse?> BuildConversationResponse(System.Data.IDbConnection connection, string conversationId, string? lang)
    {
        var conversation = await connection.QueryFirstOrDefaultAsync<ConversationRow>(Sql.Conversations.SelectForRead, new { id = conversationId });
        if (conversation is null) return null;

        var lineRows = (await connection.QueryAsync<ConversationLineRow>(Sql.ConversationLines.SelectByConversationId, new { conversationId = conversation.Id })).ToList();

        var lines = new List<ConversationLineResponse>(lineRows.Count);
        foreach (var lr in lineRows)
            lines.Add(await BuildLineResponse(connection, lr, lang));

        return new ConversationResponse
        {
            Id          = conversation.Id,
            Description = conversation.Description,
            Lines       = lines,
        };
    }

    private async Task<ConversationLineResponse> BuildLineResponse(System.Data.IDbConnection connection, ConversationLineRow lineRow, string? lang)
    {
        var wireType = ConversationLineTypeWire(lineRow.LineType);

        switch (wireType)
        {
            case "quote":
            {
                // The single query already resolves translation via LEFT JOIN + COALESCE (Sql.Quotes.SelectBase) —
                // passing lang directly is equivalent to the old TranslationLang(lang, null) wrapper, which was
                // a no-op at this call site (see #285's plan doc for the proof). No second query is needed.
                var rows = await _quoteLineRepository.QueryAsync(new { id = lineRow.QuoteId, lang });
                var effectiveRow = rows.Count > 0 ? rows[0] : null;
                var quote = effectiveRow is null ? null : ToResponse(effectiveRow, await LoadGenres(connection, effectiveRow.Id));
                return new ConversationLineResponse { Order = lineRow.Order, Type = wireType, Quote = quote };
            }
            case "stage_direction":
            {
                var sdRows = await _stageDirectionLineRepository.QueryAsync(new { id = lineRow.StageDirectionId, lang = lang ?? "en" });
                var sd = sdRows.Count > 0 ? sdRows[0] : null;
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
                var scRows = await _soundCueLineRepository.QueryAsync(new { id = lineRow.SoundCueId, lang = lang ?? "en" });
                var sc = scRows.Count > 0 ? scRows[0] : null;
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

    // Overload without text filters — used by GetAll.
    internal static (string Sql, object Parameters) BuildFilterWhere(
        string[]? types, string[]? genres, string? lang, bool unicodeAwareSearch, Guid? seriesId, Guid? universeId,
        int? yearFrom = null, int? yearTo = null)
        => BuildFilterWhere(types, genres, lang, unicodeAwareSearch, null, null, null, seriesId, universeId, yearFrom, yearTo);

    internal static (string Sql, DynamicParameters Parameters) BuildFilterWhere(
        string[]? types, string[]? genres, string? lang, bool unicodeAwareSearch,
        string? character, string? author, string? source,
        Guid? seriesId, Guid? universeId,
        int? yearFrom = null, int? yearTo = null)
    {
        var dbTypes  = types  is { Length: > 0 } ? types.Select(NormaliseType).ToArray()  : null;
        var dbGenres = genres is { Length: > 0 } ? genres.Select(NormaliseGenre).ToArray() : null;

        var clauses = new List<string> { "q.IsDeleted = 0", "s.IsDeleted = 0" };
        if (dbTypes  is not null) clauses.Add("s.Type IN @dbTypes");
        if (dbGenres is not null) clauses.Add($"EXISTS (SELECT 1 FROM Quotinator_QuoteGenre qg WHERE {IdClauses.Join("qg.QuoteId", "q.Id")} AND qg.Genre IN @dbGenres AND qg.IsDeleted = 0)");
        if (character is not null) clauses.Add(Sql.SearchField.CharacterFilter(unicodeAwareSearch));
        if (author    is not null) clauses.Add(Sql.SearchField.AuthorFilter(unicodeAwareSearch));
        if (source    is not null) clauses.Add(Sql.SearchField.SourceFilter(unicodeAwareSearch));
        // Case-insensitive (#210) via IdClauses — see docs/architecture-decisions/012-canonicalize-entity-ids-at-capture.md.
        if (seriesId  is not null) clauses.Add(IdClauses.Equals("s.SeriesId", nameof(seriesId)));
        if (universeId is not null) clauses.Add(
            $"LOWER(s.SeriesId) IN (SELECT LOWER(Id) FROM Quotinator_Series WHERE {IdClauses.Equals("UniverseId", nameof(universeId))} AND IsDeleted = 0)");
        if (yearFrom  is not null) clauses.Add("CAST(SUBSTR(s.Date, 1, 4) AS INTEGER) >= @yearFrom");
        if (yearTo    is not null) clauses.Add("CAST(SUBSTR(s.Date, 1, 4) AS INTEGER) <= @yearTo");

        var p = new DynamicParameters();
        p.Add("lang", lang);
        if (dbTypes  is not null) p.Add("dbTypes",  dbTypes);
        if (dbGenres is not null) p.Add("dbGenres", dbGenres);
        if (character is not null) p.Add("characterLike", unicodeAwareSearch ? character : $"%{character}%");
        if (author    is not null) p.Add("authorLike",    unicodeAwareSearch ? author    : $"%{author}%");
        if (source    is not null) p.Add("sourceLike",    unicodeAwareSearch ? source    : $"%{source}%");
        if (seriesId  is not null) p.Add("seriesId",   seriesId);
        if (universeId is not null) p.Add("universeId", universeId);
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

    #endregion
}

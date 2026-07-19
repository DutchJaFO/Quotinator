using Quotinator.Core.Models;
using Quotinator.Core.Import;

namespace Quotinator.Core.Services;

/// <summary>Loads <c>data/quotes.json</c> into memory at startup and serves read requests.</summary>
public sealed class QuoteService : IQuoteService
{
    private readonly IReadOnlyList<SourceQuote> _quotes;

    /// <summary>Initialises the service and loads quotes from <paramref name="dataPath"/> into memory.</summary>
    /// <param name="dataPath">Absolute or working-directory-relative path to <c>quotes.json</c>. Defaults to <c>data/quotes.json</c>.</param>
    public QuoteService(string dataPath = "data/quotes.json")
    {
        _quotes = Load(dataPath);
    }

    private static IReadOnlyList<SourceQuote> Load(string path)
    {
        if (!File.Exists(path))
            return [];

        var json = File.ReadAllText(path);
        return SourceQuoteFileReader.TryParse(json, out var quotes) ? quotes! : [];
    }

    /// <inheritdoc/>
    public QuoteResponse? GetById(string id, string? lang = null)
    {
        var quote = _quotes.FirstOrDefault(q => q.Id == id);
        return quote is null ? null : Localise(quote, lang);
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
        int? yearTo = null,
        Guid? seriesId = null,
        Guid? universeId = null)
    {
        // Flat-file SourceQuote has no Series/Universe concept (#179 is SQLite-only) — these
        // parameters are accepted to satisfy IQuoteService but have no effect on this legacy path.
        IEnumerable<SourceQuote> filtered = Filter(_quotes, types, genres, yearFrom, yearTo);

        if (character is not null)
            filtered = filtered.Where(q => q.Character is not null && Contains(q.Character, character));
        if (author is not null)
            filtered = filtered.Where(q => q.Author is not null && Contains(q.Author, author));
        if (source is not null)
            filtered = filtered.Where(q => Contains(q.Source, source));

        var pool = filtered.ToList();
        var totalMatching = pool.Count;
        var picks = pool.OrderBy(_ => Random.Shared.Next()).Take(count).Select(q => Localise(q, lang)).ToList();

        return new FilteredQuoteResult<QuoteResponse>
        {
            Status        = picks.Count > 0 ? FilteredResultStatus.Ok : FilteredResultStatus.NoResults,
            Items         = picks,
            TotalMatching = totalMatching,
        };
    }

    /// <inheritdoc/>
    public PagedResult<QuoteResponse> GetAll(int page, int pageSize, string[]? types = null, string[]? genres = null, string? lang = null, int? yearFrom = null, int? yearTo = null, Guid? seriesId = null, Guid? universeId = null)
    {
        // Flat-file SourceQuote has no Series/Universe concept (#179 is SQLite-only) — these
        // parameters are accepted to satisfy IQuoteService but have no effect on this legacy path.
        var filtered = Filter(_quotes, types, genres, yearFrom, yearTo);
        var total = filtered.Count;
        var items = filtered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(q => Localise(q, lang))
            .ToList();

        return new PagedResult<QuoteResponse>(items, page, pageSize, total);
    }

    /// <inheritdoc/>
    public FilteredQuoteResult<QuoteResponse> Search(string query, int limit, string[]? types = null, string[]? genres = null, string? lang = null, string? field = null, int? yearFrom = null, int? yearTo = null, Guid? seriesId = null, Guid? universeId = null)
    {
        // Flat-file SourceQuote has no Series/Universe concept (#179 is SQLite-only) — these
        // parameters are accepted to satisfy IQuoteService but have no effect on this legacy path.
        var filtered = Filter(_quotes, types, genres, yearFrom, yearTo);
        var items = filtered
            .Where(q => field switch
            {
                "quote"     => Contains(q.QuoteText, query),
                "source"    => Contains(q.Source, query),
                "character" => q.Character is not null && Contains(q.Character, query),
                "author"    => q.Author is not null && Contains(q.Author, query),
                _           => Contains(q.QuoteText, query)
                            || Contains(q.Source, query)
                            || (q.Character is not null && Contains(q.Character, query))
                            || (q.Author is not null && Contains(q.Author, query))
            })
            .Take(limit)
            .Select(q => Localise(q, lang))
            .ToList();

        return new FilteredQuoteResult<QuoteResponse>
        {
            Status        = items.Count > 0 ? FilteredResultStatus.Ok : FilteredResultStatus.NoResults,
            Items         = items,
            TotalMatching = items.Count,
        };
    }

    /// <inheritdoc/>
    /// <remarks>#69: this legacy v1 in-memory service only ever loads flat <see cref="SourceQuote"/> records — it has no conversation data to serve, so this always returns <c>null</c>. Nothing registers this service in the running app; the real implementation is <c>Quotinator.Core.Services.SqliteQuoteService</c>.</remarks>
    public ConversationResponse? GetConversation(string id, string? lang = null) => null;

    private static IReadOnlyList<SourceQuote> Filter(IReadOnlyList<SourceQuote> quotes, string[]? types, string[]? genres, int? yearFrom = null, int? yearTo = null)
    {
        IEnumerable<SourceQuote> result = quotes;

        if (types is { Length: > 0 })
            result = result.Where(q => types.Any(t => q.Type.ToString().Equals(t, StringComparison.OrdinalIgnoreCase)));

        if (genres is { Length: > 0 })
            result = result.Where(q => q.Genres.Any(g => genres.Any(fg => g.Equals(fg, StringComparison.OrdinalIgnoreCase))));

        if (yearFrom is not null)
            result = result.Where(q => ExtractYear(q.Date) is int y && y >= yearFrom);

        if (yearTo is not null)
            result = result.Where(q => ExtractYear(q.Date) is int y && y <= yearTo);

        return result.ToList();
    }

    private static int? ExtractYear(string? date)
    {
        if (date is null || date.Length < 4) return null;
        return int.TryParse(date.AsSpan(0, 4), out var y) ? y : null;
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static QuoteResponse Localise(SourceQuote q, string? lang)
    {
        if (lang is not null
            && !lang.Equals(q.OriginalLanguage, StringComparison.OrdinalIgnoreCase)
            && q.Translations.TryGetValue(lang, out var translation))
        {
            return new QuoteResponse
            {
                Id = q.Id,
                Quote = translation.QuoteText,
                Language = lang,
                OriginalLanguage = q.OriginalLanguage,
                Source = translation.Source ?? q.Source,
                Date = q.Date,
                Character = q.Character,
                Author = q.Author,
                Type = q.Type.ToString().ToLowerInvariant(),
                Genres = q.Genres
            };
        }

        return new QuoteResponse
        {
            Id = q.Id,
            Quote = q.QuoteText,
            Language = q.OriginalLanguage,
            OriginalLanguage = q.OriginalLanguage,
            Source = q.Source,
            Date = q.Date,
            Character = q.Character,
            Author = q.Author,
            Type = q.Type.ToString().ToLowerInvariant(),
            Genres = q.Genres
        };
    }
}

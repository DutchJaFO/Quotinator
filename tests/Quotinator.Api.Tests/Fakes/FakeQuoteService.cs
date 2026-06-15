using Quotinator.Core.Models;
using Quotinator.Core.Services;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>In-memory quote service with a fixed dataset for deterministic endpoint tests.</summary>
internal sealed class FakeQuoteService : IQuoteService
{
    internal static readonly QuoteResponse CasablancaEn = new()
    {
        Id = "aaaaaaaa-0000-0000-0000-000000000001",
        Quote = "Here's looking at you, kid.",
        Language = "en",
        OriginalLanguage = "en",
        Source = "Casablanca",
        Date = "1942",
        Character = "Rick Blaine",
        Type = "movie",
        Genres = ["drama", "romance"]
    };

    internal static readonly QuoteResponse CasablancaNl = new()
    {
        Id = "aaaaaaaa-0000-0000-0000-000000000001",
        Quote = "Hier kijk ik naar je, kind.",
        Language = "nl",
        OriginalLanguage = "en",
        Source = "Casablanca",
        Date = "1942",
        Character = "Rick Blaine",
        Type = "movie",
        Genres = ["drama", "romance"]
    };

    internal static readonly QuoteResponse Terminator = new()
    {
        Id = "aaaaaaaa-0000-0000-0000-000000000002",
        Quote = "I'll be back.",
        Language = "en",
        OriginalLanguage = "en",
        Source = "The Terminator",
        Date = "1984",
        Character = "The Terminator",
        Type = "movie",
        Genres = ["action", "sci-fi"]
    };

    internal static readonly QuoteResponse Churchill = new()
    {
        Id = "aaaaaaaa-0000-0000-0000-000000000003",
        Quote = "We shall fight on the beaches.",
        Language = "en",
        OriginalLanguage = "en",
        Source = "House of Commons, 4 June 1940",
        Date = "1940-06-04",
        Author = "Winston Churchill",
        Type = "person",
        Genres = ["non-fiction"]
    };

    internal static readonly QuoteResponse Tolkien = new()
    {
        Id = "aaaaaaaa-0000-0000-0000-000000000004",
        Quote = "Not all those who wander are lost.",
        Language = "en",
        OriginalLanguage = "en",
        Source = "The Fellowship of the Ring",
        Date = "1954",
        Character = "Gandalf",
        Author = "J.R.R. Tolkien",
        Type = "book",
        Genres = ["fantasy", "fiction"]
    };

    private static readonly IReadOnlyList<QuoteResponse> All = [CasablancaEn, Terminator, Churchill, Tolkien];

    public QuoteResponse? GetById(string id, string? lang = null)
    {
        if (id == CasablancaEn.Id && lang == "nl") return CasablancaNl;
        return All.FirstOrDefault(q => q.Id == id);
    }

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
        IEnumerable<QuoteResponse> filtered = All;

        if (types is { Length: > 0 })
            filtered = filtered.Where(q => types.Any(t => q.Type.Equals(t, StringComparison.OrdinalIgnoreCase)));
        if (genres is { Length: > 0 })
            filtered = filtered.Where(q => q.Genres.Any(g => genres.Any(fg => g.Equals(fg, StringComparison.OrdinalIgnoreCase))));
        if (character is not null)
            filtered = filtered.Where(q => q.Character?.Contains(character, StringComparison.OrdinalIgnoreCase) ?? false);
        if (author is not null)
            filtered = filtered.Where(q => q.Author?.Contains(author, StringComparison.OrdinalIgnoreCase) ?? false);
        if (source is not null)
            filtered = filtered.Where(q => q.Source.Contains(source, StringComparison.OrdinalIgnoreCase));
        if (yearFrom is not null)
            filtered = filtered.Where(q => ExtractYear(q.Date) is int y && y >= yearFrom);
        if (yearTo is not null)
            filtered = filtered.Where(q => ExtractYear(q.Date) is int y && y <= yearTo);

        var pool  = filtered.ToList();
        var items = pool.Take(count).ToList();
        return new FilteredQuoteResult<QuoteResponse>
        {
            Status        = items.Count > 0 ? FilteredResultStatus.Ok : FilteredResultStatus.NoResults,
            Items         = items,
            TotalMatching = pool.Count,
        };
    }

    public PagedResult<QuoteResponse> GetAll(int page, int pageSize, string[]? types = null, string[]? genres = null, string? lang = null, int? yearFrom = null, int? yearTo = null)
    {
        var items = All
            .Where(q => types is not { Length: > 0 } || types.Any(t => q.Type.Equals(t, StringComparison.OrdinalIgnoreCase)))
            .Where(q => genres is not { Length: > 0 } || q.Genres.Any(g => genres.Any(fg => g.Equals(fg, StringComparison.OrdinalIgnoreCase))))
            .Where(q => yearFrom is null || (ExtractYear(q.Date) is int y1 && y1 >= yearFrom))
            .Where(q => yearTo   is null || (ExtractYear(q.Date) is int y2 && y2 <= yearTo))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new PagedResult<QuoteResponse>(items, page, pageSize, All.Count);
    }

    public IReadOnlyList<QuoteResponse> Search(string query, int limit, string[]? types = null, string[]? genres = null, string? lang = null, string? field = null, int? yearFrom = null, int? yearTo = null) =>
        All.Where(q => field switch
            {
                "quote"     => q.Quote.Contains(query, StringComparison.OrdinalIgnoreCase),
                "source"    => q.Source.Contains(query, StringComparison.OrdinalIgnoreCase),
                "character" => q.Character?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false,
                "author"    => q.Author?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false,
                _           => q.Quote.Contains(query, StringComparison.OrdinalIgnoreCase)
                            || q.Source.Contains(query, StringComparison.OrdinalIgnoreCase)
            })
           .Where(q => types is not { Length: > 0 } || types.Any(t => q.Type.Equals(t, StringComparison.OrdinalIgnoreCase)))
           .Where(q => genres is not { Length: > 0 } || q.Genres.Any(g => genres.Any(fg => g.Equals(fg, StringComparison.OrdinalIgnoreCase))))
           .Where(q => yearFrom is null || (ExtractYear(q.Date) is int y1 && y1 >= yearFrom))
           .Where(q => yearTo   is null || (ExtractYear(q.Date) is int y2 && y2 <= yearTo))
           .Take(limit)
           .ToList();

    private static int? ExtractYear(string? date)
    {
        if (date is null || date.Length < 4) return null;
        return int.TryParse(date.AsSpan(0, 4), out var y) ? y : null;
    }
}

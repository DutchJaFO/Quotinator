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

    private static readonly IReadOnlyList<QuoteResponse> All = [CasablancaEn, Terminator, Churchill];

    public QuoteResponse? GetById(string id, string? lang = null)
    {
        if (id == CasablancaEn.Id && lang == "nl") return CasablancaNl;
        return All.FirstOrDefault(q => q.Id == id);
    }

    public IReadOnlyList<QuoteResponse> GetRandom(int count, string? lang = null) =>
        All.Take(count).ToList();

    public PagedResult<QuoteResponse> GetAll(int page, int pageSize, string? type = null, string? genre = null, string? lang = null)
    {
        var items = All
            .Where(q => type is null || q.Type == type)
            .Where(q => genre is null || q.Genres.Contains(genre))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new PagedResult<QuoteResponse>(items, page, pageSize, All.Count);
    }

    public IReadOnlyList<QuoteResponse> Search(string query, int limit, string? type = null, string? genre = null, string? lang = null) =>
        All.Where(q => q.Quote.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || q.Source.Contains(query, StringComparison.OrdinalIgnoreCase))
           .Take(limit)
           .ToList();
}

using System.Text.Json;
using Quotinator.Core.Models;

namespace Quotinator.Core.Services;

/// <summary>Loads <c>data/quotes.json</c> into memory at startup and serves read requests.</summary>
public sealed class QuoteService : IQuoteService
{
    private readonly IReadOnlyList<Quote> _quotes;

    /// <param name="dataPath">Absolute or working-directory-relative path to <c>quotes.json</c>. Defaults to <c>data/quotes.json</c>.</param>
    public QuoteService(string dataPath = "data/quotes.json")
    {
        _quotes = Load(dataPath);
    }

    private static IReadOnlyList<Quote> Load(string path)
    {
        if (!File.Exists(path))
            return [];

        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<List<Quote>>(json, options) ?? [];
    }

    /// <inheritdoc/>
    public QuoteResponse? GetById(string id, string? lang = null)
    {
        var quote = _quotes.FirstOrDefault(q => q.Id == id);
        return quote is null ? null : Localise(quote, lang);
    }

    /// <inheritdoc/>
    public IReadOnlyList<QuoteResponse> GetRandom(int count, string? lang = null)
    {
        if (_quotes.Count == 0) return [];
        var picks = _quotes.OrderBy(_ => Random.Shared.Next()).Take(count);
        return picks.Select(q => Localise(q, lang)).ToList();
    }

    /// <inheritdoc/>
    public PagedResult<QuoteResponse> GetAll(int page, int pageSize, string? type = null, string? genre = null, string? lang = null)
    {
        var filtered = Filter(_quotes, type, genre);
        var total = filtered.Count;
        var items = filtered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(q => Localise(q, lang))
            .ToList();

        return new PagedResult<QuoteResponse>(items, page, pageSize, total);
    }

    /// <inheritdoc/>
    public IReadOnlyList<QuoteResponse> Search(string query, int limit, string? type = null, string? genre = null, string? lang = null)
    {
        var filtered = Filter(_quotes, type, genre);
        return filtered
            .Where(q => Contains(q.QuoteText, query)
                     || Contains(q.Source, query)
                     || (q.Character is not null && Contains(q.Character, query))
                     || (q.Author is not null && Contains(q.Author, query)))
            .Take(limit)
            .Select(q => Localise(q, lang))
            .ToList();
    }

    private static IReadOnlyList<Quote> Filter(IReadOnlyList<Quote> quotes, string? type, string? genre)
    {
        IEnumerable<Quote> result = quotes;

        if (type is not null)
            result = result.Where(q => q.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

        if (genre is not null)
            result = result.Where(q => q.Genres.Any(g => g.Equals(genre, StringComparison.OrdinalIgnoreCase)));

        return result.ToList();
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static QuoteResponse Localise(Quote q, string? lang)
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
                Type = q.Type,
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
            Type = q.Type,
            Genres = q.Genres
        };
    }
}

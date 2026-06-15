using Quotinator.Core.Models;

namespace Quotinator.Core.Services;

/// <summary>Read-only access to the quote dataset.</summary>
public interface IQuoteService
{
    /// <summary>Returns the quote with the given ID, localised to <paramref name="lang"/> if a translation exists. Returns <c>null</c> if not found.</summary>
    QuoteResponse? GetById(string id, string? lang = null);

    /// <summary>
    /// Returns <paramref name="count"/> quotes chosen at random from the filtered pool, wrapped in a result envelope.
    /// Pass <paramref name="types"/>, <paramref name="genres"/>, <paramref name="character"/>, <paramref name="author"/>, or <paramref name="source"/>
    /// to restrict the pool before random selection. Omit all filters to draw from the full dataset.
    /// </summary>
    FilteredQuoteResult<QuoteResponse> GetRandom(
        int count,
        string[]? types = null,
        string[]? genres = null,
        string? character = null,
        string? author = null,
        string? source = null,
        string? lang = null,
        int? yearFrom = null,
        int? yearTo = null);

    /// <summary>Returns a paginated slice of all quotes, with optional multi-value type, genre, and year filters.</summary>
    PagedResult<QuoteResponse> GetAll(int page, int pageSize, string[]? types = null, string[]? genres = null, string? lang = null, int? yearFrom = null, int? yearTo = null);

    /// <summary>Returns quotes whose text, source, character, or author contain <paramref name="query"/> (case-insensitive). Pass <paramref name="field"/> to restrict which field is searched.</summary>
    IReadOnlyList<QuoteResponse> Search(string query, int limit, string[]? types = null, string[]? genres = null, string? lang = null, string? field = null, int? yearFrom = null, int? yearTo = null);
}

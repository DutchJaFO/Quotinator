using Quotinator.Core.Models;

namespace Quotinator.Core.Services;

/// <summary>Read-only access to the quote dataset.</summary>
public interface IQuoteService
{
    /// <summary>Returns the quote with the given ID, localised to <paramref name="lang"/> if a translation exists. Returns <c>null</c> if not found.</summary>
    QuoteResponse? GetById(string id, string? lang = null);

    /// <summary>Returns <paramref name="count"/> quotes chosen at random, localised to <paramref name="lang"/>.</summary>
    IReadOnlyList<QuoteResponse> GetRandom(int count, string? lang = null);

    /// <summary>Returns a paginated slice of all quotes, with optional type and genre filters.</summary>
    PagedResult<QuoteResponse> GetAll(int page, int pageSize, string? type = null, string? genre = null, string? lang = null);

    /// <summary>Returns quotes whose text, source, character, or author contain <paramref name="query"/> (case-insensitive). Pass <paramref name="field"/> to restrict which field is searched.</summary>
    IReadOnlyList<QuoteResponse> Search(string query, int limit, string? type = null, string? genre = null, string? lang = null, string? field = null);
}

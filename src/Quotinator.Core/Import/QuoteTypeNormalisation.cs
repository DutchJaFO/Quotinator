using Quotinator.Core.Models;

namespace Quotinator.Core.Import;

/// <summary>Normalises a raw upstream <c>type</c> value to one of Quotinator's canonical quote types. Ported verbatim from the historical <c>scripts/seed.csx</c> algorithm.</summary>
public static class QuoteTypeNormalisation
{
    /// <summary>Maps <paramref name="raw"/> (case-insensitive) to a recognised canonical <see cref="QuoteType"/>, falling back to <paramref name="defaultType"/> when unrecognised or <c>null</c>.</summary>
    public static QuoteType CanonicalType(string? raw, QuoteType defaultType) => raw?.ToLowerInvariant() switch
    {
        "movie"  => QuoteType.Movie,
        "tv"     => QuoteType.Tv,
        "anime"  => QuoteType.Anime,
        "book"   => QuoteType.Book,
        "person" => QuoteType.Person,
        _        => defaultType
    };
}

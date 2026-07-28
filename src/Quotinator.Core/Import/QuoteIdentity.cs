using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Quotinator.Core.Import;

/// <summary>
/// Generates the stable, deterministic <see cref="SourceQuote.Id"/> used for quotes converted from
/// external sources that have no identifier of their own. Ported verbatim from the historical
/// <c>scripts/seed.csx</c> algorithm — must never change, since the same quote/source pair must always
/// produce the same id across every re-conversion, or existing database rows would be silently
/// duplicated or orphaned on the next refresh.
/// </summary>
public static class QuoteIdentity
{
    /// <summary>Trims, lowercases, and collapses runs of whitespace to a single space.</summary>
    public static string Normalise(string s) =>
        Regex.Replace(s.Trim().ToLowerInvariant(), @"\s+", " ");

    /// <summary>
    /// Derives a stable UUID v4 string from the normalised <paramref name="quote"/> and
    /// <paramref name="source"/> text — SHA-256 of <c>"{quote}|{source}"</c>, truncated to 16 bytes with
    /// the UUID version/variant bits forced.
    /// </summary>
    public static string StableId(string quote, string source)
    {
        var key  = $"{Normalise(quote)}|{Normalise(source)}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        hash[6] = (byte)((hash[6] & 0x0f) | 0x40);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        return new Guid(hash[..16]).ToString();
    }
}

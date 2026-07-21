namespace Quotinator.Data.Helpers;

/// <summary>
/// Canonicalizes a raw externally-supplied entity id (e.g. a file-authored explicit id) to this
/// project's single stored/presented id convention, per ADR 012. Every entity type — Quote, Source,
/// Person, StageDirection, SoundCue, Conversation, Character, Series, Universe — canonicalizes to
/// lowercase, matching <c>Guid.ToString("D")</c>'s own default format and <c>GuidHandler</c>'s
/// convention. This project went through two prior conventions before settling here: originally Quote
/// alone was lowercase while every other entity was uppercase (a historical accident of
/// <c>QuoteIdentity.StableId</c> predating the uppercase convention), then briefly unified everything
/// to uppercase, before a final developer review chose lowercase for the whole system instead —
/// readability, not the read-side case-insensitivity mechanism, drove that choice. `LOWER(...)`
/// wrapping in SQL comparisons (<see cref="Quotinator.Data.Diagnostics.SqlIdCaseGuard"/>,
/// <see cref="Quotinator.Data.Queries.IdClauses"/>) is unrelated to and unaffected by this convention —
/// it exists purely so a comparison matches regardless of casing, independent of which casing is
/// actually canonical.
/// </summary>
public static class EntityIdCanonicalizer
{
    /// <exception cref="FormatException"><paramref name="rawId"/> is not a valid Guid.</exception>
    public static string CanonicalizeLowercase(string rawId)
        => Guid.Parse(rawId).ToString("D");

    /// <summary>Non-throwing form — a capture site must fall back gracefully on a malformed id, not abort an entire import batch.</summary>
    public static bool TryCanonicalizeLowercase(string rawId, out string? canonical)
    {
        if (Guid.TryParse(rawId, out var parsed))
        {
            canonical = parsed.ToString("D");
            return true;
        }

        canonical = null;
        return false;
    }
}

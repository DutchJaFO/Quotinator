namespace Quotinator.Data.Helpers;

/// <summary>
/// Canonicalizes a raw externally-supplied entity id (e.g. a file-authored explicit id) to this
/// project's stored-id convention, per ADR 012. Source/Person/StageDirection/SoundCue/Conversation
/// canonicalize to uppercase, matching <c>EntityIdentity.StableId</c>'s own convention; Quote
/// canonicalizes to lowercase, matching <c>QuoteIdentity.StableId</c>'s pinned convention.
/// </summary>
public static class EntityIdCanonicalizer
{
    /// <exception cref="FormatException"><paramref name="rawId"/> is not a valid Guid.</exception>
    public static string CanonicalizeUppercase(string rawId)
        => Guid.Parse(rawId).ToString("D").ToUpperInvariant();

    /// <summary>Non-throwing form — a capture site must fall back gracefully on a malformed id, not abort an entire import batch.</summary>
    public static bool TryCanonicalizeUppercase(string rawId, out string? canonical)
    {
        if (Guid.TryParse(rawId, out var parsed))
        {
            canonical = parsed.ToString("D").ToUpperInvariant();
            return true;
        }

        canonical = null;
        return false;
    }
}

using System.Security.Cryptography;
using System.Text;

namespace Quotinator.Data.Notifications;

/// <summary>
/// Computes the short content hash a notification payload stores in
/// <see cref="NotificationMetadataDto.ContentHash"/> (#312).
/// <para>
/// One implementation rather than one per producer, because the value only means anything if every
/// producer — and every migration backfilling a row an earlier producer wrote — derives it the same
/// way. A second copy of "SHA-256, take the first eight hex characters" that drifted by one character
/// would not fail; it would silently re-announce every affected notification instead.
/// </para>
/// </summary>
public static class NotificationContentHash
{
    /// <summary>
    /// The hash of <paramref name="content"/>: SHA-256 over its UTF-8 bytes, rendered as uppercase hex
    /// and truncated to eight characters.
    /// <para>
    /// Truncated because this is change detection, not integrity protection — it answers "is this the
    /// same text I already showed?", where a collision costs one suppressed notification rather than
    /// anything security-relevant.
    /// </para>
    /// </summary>
    /// <param name="content">The text whose changes should re-announce the notification.</param>
    public static string Of(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..8];
}

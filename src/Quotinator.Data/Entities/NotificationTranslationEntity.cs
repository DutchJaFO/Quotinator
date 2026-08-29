using Dapper.Contrib.Extensions;
using Quotinator.Data.Models;

namespace Quotinator.Data.Entities;

/// <summary>
/// A translated <see cref="NotificationEntity.Title"/>/<see cref="NotificationEntity.Body"/> for one
/// language (#319) — the sibling of <c>Quotinator_QuoteTranslation</c>, and the same arrangement.
/// <para>
/// This table holds only languages *other* than the notification's own
/// <c>OriginalLanguage</c>; the original text never leaves <c>System_Notification</c>. The read path
/// resolves <c>COALESCE(translation, original)</c>, which depends on that, and it is also what keeps
/// each producer's content hash — taken over the original body — unaffected by translation.
/// </para>
/// </summary>
[Table("System_NotificationTranslation")]
public sealed class NotificationTranslationEntity : RecordBase
{
    /// <summary>The notification this translation belongs to.</summary>
    public Guid NotificationId { get; init; }

    /// <summary>ISO 639-1 language code of the translation (e.g. "nl", "de").</summary>
    public string Language { get; init; } = string.Empty;

    /// <summary>
    /// The translated headline, or <see langword="null"/> when this language translates only the body.
    /// Independent of <see cref="Body"/>: the read path's fallback is per-field, so a null here falls
    /// back to the original title rather than dropping the translation entirely.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>The translated message text.</summary>
    public string Body { get; init; } = string.Empty;
}

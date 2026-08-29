namespace Quotinator.Data.Notifications;

/// <summary>
/// One language's translated title and body, supplied by a producer at write time (#319).
/// <para>
/// Deliberately unsuffixed. ADR 016's <c>Dto</c> suffix means a wire-format shape — an on-disk JSON
/// file, or a JSON blob serialized into a database column, as <see cref="NotificationMetadataDto"/>
/// genuinely is. This type is neither: it is never serialized, only carried from a producer into
/// <c>INotificationWriter.WriteAsync</c>, where each instance becomes a
/// <c>System_NotificationTranslation</c> row. That places it in ADR 016's explicitly out-of-scope
/// category alongside <c>MasterDataReference</c> and <c>SeedBatch</c>.
/// </para>
/// <para>
/// A list of these rather than a <c>Dictionary&lt;string, string&gt;</c>, because
/// <see cref="Title"/> is nullable and independent of <see cref="Body"/>, which a dictionary keyed by
/// language cannot express without a second lookup.
/// </para>
/// <para>
/// Never supply the notification's own original language here. The original title and body stay on the
/// notification row itself; this carries the *other* languages only.
/// </para>
/// </summary>
/// <param name="Language">ISO 639-1 language code this translation is written in (e.g. "nl", "de").</param>
/// <param name="Title">The translated headline, or <see langword="null"/> to fall back to the original title.</param>
/// <param name="Body">The translated message text.</param>
public readonly record struct NotificationTranslation(string Language, string? Title, string Body);

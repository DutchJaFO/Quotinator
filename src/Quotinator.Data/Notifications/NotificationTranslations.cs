namespace Quotinator.Data.Notifications;

/// <summary>
/// Builds the per-language title/body set a notification producer hands to
/// <c>INotificationWriter.WriteAsync</c> (#319), from the same <c>i18ntext/UI.*.json</c> files every
/// other user-facing string comes from.
/// <para>
/// Resolves no culture. A producer runs at startup, where there is no request culture to resolve
/// against — which is exactly why a notification stores its text per language instead of being
/// rendered per request like a UI string.
/// </para>
/// <para>
/// Lives in <c>Quotinator.Data</c> rather than <c>Quotinator.Api.Startup</c>, where #319 first put it.
/// Notifications are a feature of the database system, not of Quotinator's own domain — ADR 018 makes
/// <c>System_Notification</c> the reference implementation of Data-owned system content, and nothing
/// about assembling a notification's text is Quotinator-specific. The practical driver is the same one
/// that moved <see cref="NotificationSeeding"/> here in #312: a producer in <c>Quotinator.Core</c>
/// cannot reach into <c>Quotinator.Api</c> (the dependency runs Api → Core, never the reverse), so a
/// helper stranded in the Api layer would have to be reimplemented for every Core-side producer. Its
/// text comes from <see cref="INotificationTextSource"/> rather than <c>IApiLocalizer</c> for the
/// matching reason on the other side — see that interface for why the dependency is inverted.
/// </para>
/// </summary>
public static class NotificationTranslations
{
    /// <summary>The language a producer writes its original title and body in.</summary>
    public const string OriginalLanguage = "en";

    /// <summary>
    /// Returns a key resolved in <see cref="OriginalLanguage"/> specifically — the text stored on the
    /// notification row itself.
    /// <para>
    /// Not a current-culture lookup: that resolves <see cref="System.Globalization.CultureInfo.CurrentUICulture"/>,
    /// which at startup is whatever the host happens to default to. The stored original has to be the
    /// language <see cref="OriginalLanguage"/> claims it is, or the read path's fallback returns text in
    /// a language it then mislabels.
    /// </para>
    /// </summary>
    /// <param name="textSource">Supplies the key in every language.</param>
    /// <param name="key">The message key to resolve.</param>
    /// <param name="args">Positional arguments substituted into the template.</param>
    public static string Original(INotificationTextSource textSource, string key, params object[] args)
        => textSource.ForEveryLanguage(key, args).TryGetValue(OriginalLanguage, out string? value) ? value : key;

    /// <summary>
    /// Returns one <see cref="NotificationTranslation"/> per language that has both a title and a body
    /// for the given keys, excluding <see cref="OriginalLanguage"/>.
    /// <para>
    /// English is excluded deliberately: it is the notification's own text, stored on
    /// <c>System_Notification</c> itself. A translation row for the original language would be a second
    /// copy of the same words, free to drift from the copy the read path falls back to.
    /// </para>
    /// <para>
    /// A language missing either key contributes nothing rather than a half-populated row. The read
    /// path then falls back to the original for that language and reports it honestly as untranslated,
    /// which is a better answer than a translated title above an English body.
    /// </para>
    /// </summary>
    /// <param name="textSource">Supplies each key in every language.</param>
    /// <param name="titleKey">Message key for the notification's title.</param>
    /// <param name="bodyKey">Message key for the notification's body.</param>
    /// <param name="titleArgs">Positional arguments substituted into the title, in every language.</param>
    /// <param name="bodyArgs">Positional arguments substituted into the body, in every language.</param>
    public static IReadOnlyList<NotificationTranslation> Build(
        INotificationTextSource textSource,
        string titleKey,
        string bodyKey,
        object[]? titleArgs = null,
        object[]? bodyArgs = null)
    {
        IReadOnlyDictionary<string, string> titles = textSource.ForEveryLanguage(titleKey, titleArgs ?? []);
        IReadOnlyDictionary<string, string> bodies = textSource.ForEveryLanguage(bodyKey, bodyArgs ?? []);

        List<NotificationTranslation> translations = [];

        foreach (KeyValuePair<string, string> body in bodies)
        {
            if (string.Equals(body.Key, OriginalLanguage, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!titles.TryGetValue(body.Key, out string? title))
                continue;

            translations.Add(new NotificationTranslation(body.Key, title, body.Value));
        }

        return translations;
    }
}

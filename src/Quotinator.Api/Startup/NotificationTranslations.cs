using Quotinator.Core.Services;
using Quotinator.Data.Notifications;

namespace Quotinator.Api.Startup;

/// <summary>
/// Builds the per-language title/body set a notification producer hands to
/// <c>INotificationWriter.WriteAsync</c> (#319), from the same <c>i18ntext/UI.*.json</c> files every
/// other user-facing string comes from.
/// <para>
/// Resolves no culture. A producer runs at startup, where there is no request culture to resolve
/// against — which is exactly why a notification stores its text per language instead of being
/// rendered per request like a UI string.
/// </para>
/// </summary>
internal static class NotificationTranslations
{
    /// <summary>The language a producer writes its original title and body in.</summary>
    internal const string OriginalLanguage = "en";

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
    /// <summary>
    /// Returns a key resolved in <see cref="OriginalLanguage"/> specifically — the text stored on the
    /// notification row itself.
    /// <para>
    /// Not <c>localizer[key]</c>: that resolves <see cref="System.Globalization.CultureInfo.CurrentUICulture"/>,
    /// which at startup is whatever the host happens to default to. The stored original has to be the
    /// language <c>OriginalLanguage</c> claims it is, or the read path's fallback returns text in a
    /// language it then mislabels.
    /// </para>
    /// </summary>
    /// <param name="localizer">Supplies the key in every language.</param>
    /// <param name="key">The message key to resolve.</param>
    /// <param name="args">Positional arguments substituted into the template.</param>
    internal static string Original(IApiLocalizer localizer, string key, params object[] args)
        => localizer.ForEveryLanguage(key, args).TryGetValue(OriginalLanguage, out string? value) ? value : key;

    /// <param name="localizer">Supplies each key in every language.</param>
    /// <param name="titleKey">Message key for the notification's title.</param>
    /// <param name="bodyKey">Message key for the notification's body.</param>
    /// <param name="titleArgs">Positional arguments substituted into the title, in every language.</param>
    /// <param name="bodyArgs">Positional arguments substituted into the body, in every language.</param>
    internal static IReadOnlyList<NotificationTranslation> Build(
        IApiLocalizer localizer,
        string titleKey,
        string bodyKey,
        object[]? titleArgs = null,
        object[]? bodyArgs = null)
    {
        IReadOnlyDictionary<string, string> titles = localizer.ForEveryLanguage(titleKey, titleArgs ?? []);
        IReadOnlyDictionary<string, string> bodies = localizer.ForEveryLanguage(bodyKey, bodyArgs ?? []);

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

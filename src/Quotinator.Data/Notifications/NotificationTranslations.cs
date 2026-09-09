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

    /// <summary>
    /// Composes one sentence fragment per language from <paramref name="parts"/>, joined with that
    /// language's own separator and final conjunction (#377).
    /// </summary>
    /// <param name="textSource">Resolves each key in every available language.</param>
    /// <param name="parts">The fragments to include, in reading order — each a key and its arguments. Already filtered by the caller; an empty list yields an empty fragment per language.</param>
    /// <param name="separatorKey">Key for the separator between all but the last two parts (typically <c>", "</c>).</param>
    /// <param name="finalJoinKey">Key for the join before the last part (typically <c>" and "</c>).</param>
    /// <returns>Language code to composed fragment, for every language that could resolve all of it.</returns>
    /// <remarks>
    /// A composed list cannot be one template string per language: which fragments appear depends on the
    /// data, and the separator and the final conjunction are themselves language-specific. Composing
    /// per language keeps both facts in the translation files rather than hard-coding English
    /// punctuation and word order into the producer.
    /// <para>
    /// Every fragment is resolved for every language *before* joining, so a language missing one of them
    /// drops out entirely rather than contributing a sentence with an untranslated clause in it — the
    /// same all-or-nothing rule <see cref="Build(INotificationTextSource, string, string, object[], object[])"/>
    /// applies to a title/body pair.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, string> ComposeForEveryLanguage(
        INotificationTextSource textSource,
        IReadOnlyList<(string Key, object[] Args)> parts,
        string separatorKey,
        string finalJoinKey)
    {
        ArgumentNullException.ThrowIfNull(textSource);
        ArgumentNullException.ThrowIfNull(parts);

        IReadOnlyDictionary<string, string> separators = textSource.ForEveryLanguage(separatorKey);
        IReadOnlyDictionary<string, string> finalJoins = textSource.ForEveryLanguage(finalJoinKey);

        List<IReadOnlyDictionary<string, string>> resolved =
            [.. parts.Select(p => textSource.ForEveryLanguage(p.Key, p.Args))];

        Dictionary<string, string> composed = [];
        foreach (string language in separators.Keys)
        {
            if (!finalJoins.TryGetValue(language, out string? finalJoin))
                continue;

            List<string> fragments = [];
            bool complete = true;
            foreach (IReadOnlyDictionary<string, string> part in resolved)
            {
                if (!part.TryGetValue(language, out string? fragment)) { complete = false; break; }
                fragments.Add(fragment);
            }

            if (!complete) continue;

            composed[language] = fragments.Count switch
            {
                0 => string.Empty,
                1 => fragments[0],
                _ => string.Join(separators[language], fragments[..^1]) + finalJoin + fragments[^1],
            };
        }

        return composed;
    }

    /// <summary>
    /// As <see cref="Build(INotificationTextSource, string, string, object[], object[])"/>, but with the
    /// body's arguments varying per language — needed when one of them is itself composed text (#377).
    /// </summary>
    /// <param name="textSource">Resolves each key in every available language.</param>
    /// <param name="titleKey">Key for the notification's title.</param>
    /// <param name="bodyKey">Key for the notification's body.</param>
    /// <param name="titleArgs">Arguments for the title, the same in every language.</param>
    /// <param name="bodyArgsByLanguage">Body arguments per language code. A language absent here contributes no translation.</param>
    public static IReadOnlyList<NotificationTranslation> Build(
        INotificationTextSource textSource,
        string titleKey,
        string bodyKey,
        object[]? titleArgs,
        IReadOnlyDictionary<string, object[]> bodyArgsByLanguage)
    {
        ArgumentNullException.ThrowIfNull(textSource);
        ArgumentNullException.ThrowIfNull(bodyArgsByLanguage);

        IReadOnlyDictionary<string, string> titles = textSource.ForEveryLanguage(titleKey, titleArgs ?? []);

        List<NotificationTranslation> translations = [];
        foreach (KeyValuePair<string, object[]> languageArgs in bodyArgsByLanguage)
        {
            if (string.Equals(languageArgs.Key, OriginalLanguage, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!titles.TryGetValue(languageArgs.Key, out string? title))
                continue;

            if (!textSource.ForEveryLanguage(bodyKey, languageArgs.Value).TryGetValue(languageArgs.Key, out string? body))
                continue;

            translations.Add(new NotificationTranslation(languageArgs.Key, title, body));
        }

        return translations;
    }
}

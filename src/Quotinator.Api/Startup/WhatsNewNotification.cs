using Quotinator.Changelog.Enums;
using Quotinator.Constants.Api;
using Quotinator.Core.Services;
using Quotinator.Changelog.Models;
using Quotinator.Data.Enums;
using Quotinator.Data.Notifications;
using Quotinator.Data.Repositories;

namespace Quotinator.Api.Startup;

/// <summary>
/// Third producer for #278's notification mechanism (alongside #279's and #289's) — announces every
/// release's notification-flagged changelog highlights (#307's
/// <c>ChangelogReservedAudience.Notification</c> convention) missed since the last version this app
/// instance actively ran, one notification per release, each showing its own version — plus the
/// <c>unreleased</c> entry's own flagged highlights, always considered regardless of version, since
/// <c>unreleased</c> is effectively "the current version" ahead of the last tagged release (per
/// developer direction: a real release never carries an <c>unreleased</c> section of its own). "Seen"
/// state is the existing server-side notification history itself (#278) — no separate cookie or
/// <c>localStorage</c> marker is needed.
/// </summary>
internal static class WhatsNewNotification
{
    /// <summary>One notification's worth of content, ready to hand to <see cref="NotificationSeeding.SeedOnceAsync"/>.</summary>
    /// <param name="Metadata">Identifies the notification and is stored alongside it — no composed key string is involved.</param>
    /// <param name="Title">Short headline.</param>
    /// <param name="Body">The flagged highlights, one per line.</param>
    /// <param name="Translations">The same title and body in every other language (#319), built from that language's own changelog document.</param>
    internal readonly record struct Seed(
        WhatsNewMetadataDto Metadata,
        string Title,
        string Body,
        IReadOnlyList<NotificationTranslation> Translations);

    /// <summary>
    /// Builds one notification per release with notification-flagged highlights in the range this app
    /// instance missed, plus one for <paramref name="document"/>'s own <c>unreleased</c> entry when it
    /// has flagged highlights. <paramref name="lastActiveVersion"/> is <see langword="null"/> on a
    /// genuinely fresh install (no prior startup ever recorded a version) — in that case only
    /// <paramref name="currentVersion"/> is considered from <see cref="ChangelogDocument.Releases"/>,
    /// never the full changelog history. Otherwise every release strictly newer than
    /// <paramref name="lastActiveVersion"/> up to and including <paramref name="currentVersion"/> is
    /// considered, using <see cref="ChangelogDocument.Releases"/>' own newest-first array order rather
    /// than parsing semver — this project already treats that order as authoritative. Pure function of
    /// its inputs, kept separate from <see cref="SeedAsync"/> so it is unit-testable without a real
    /// <see cref="INotificationReader"/>/<see cref="INotificationWriter"/>.
    /// </summary>
    internal static List<Seed> BuildSeeds(
        ChangelogDocument? document,
        string? lastActiveVersion,
        string currentVersion,
        IApiLocalizer? localizer = null,
        IReadOnlyDictionary<string, ChangelogDocument>? translatedDocuments = null)
    {
        if (document is null)
            return [];

        List<Seed> seeds = [];

        Seed? unreleasedSeed = BuildUnreleasedSeed(document.Unreleased, localizer, translatedDocuments);
        if (unreleasedSeed is not null)
            seeds.Add(unreleasedSeed.Value);

        IReadOnlyList<ChangelogRelease> releases = document.Releases; // newest first, by convention

        IEnumerable<ChangelogRelease> candidates;
        if (lastActiveVersion is null)
        {
            candidates = releases.Where(r => r.Version == currentVersion);
        }
        else
        {
            int currentIndex = IndexOfVersion(releases, currentVersion);
            int lastActiveIndex = IndexOfVersion(releases, lastActiveVersion);

            if (currentIndex < 0)
                candidates = []; // the running version isn't in the changelog at all
            else if (lastActiveIndex < 0)
                // lastActiveVersion predates the changelog's own history (or was otherwise never
                // recorded there) — fall back to just the current version rather than guessing how
                // far back to walk.
                candidates = releases.Where(r => r.Version == currentVersion);
            else
                // Skip/Take handles "no upgrade" (currentIndex == lastActiveIndex, Take(0)) and a
                // downgrade (currentIndex > lastActiveIndex, negative count) the same way: nothing to
                // report — .NET's own Take(int) treats a non-positive count as an empty sequence.
                candidates = releases.Skip(currentIndex).Take(lastActiveIndex - currentIndex);
        }

        foreach (ChangelogRelease release in candidates)
        {
            List<string> highlights = release.GetHighlightsFor(ChangelogReservedAudience.Notification);
            if (highlights.Count == 0)
                continue;

            // A released version identifies itself: its highlights are frozen once tagged, so the
            // version alone is the identity. Nothing is concatenated, and nothing is embedded in the
            // body — which is what makes the old "1.9.1 matches inside 1.9.10" hazard structurally
            // impossible rather than merely worked around.
            seeds.Add(new Seed(
                new WhatsNewMetadataDto { ReleaseState = NotificationReleaseState.Released, Version = release.Version },
                Title: TitleFor(localizer, ApiMessages.NotificationWhatsNewReleasedTitle, release.Version),
                Body:  string.Join('\n', highlights),
                Translations: BuildTranslations(
                    localizer, translatedDocuments,
                    ApiMessages.NotificationWhatsNewReleasedTitle, [release.Version],
                    doc => doc.Releases.FirstOrDefault(r => r.Version == release.Version)
                                       ?.GetHighlightsFor(ChangelogReservedAudience.Notification))));
        }

        return seeds;
    }

    /// <summary>Writes one notification per seed returned by <see cref="BuildSeeds"/>, skipping any already seeded.</summary>
    /// <param name="reader">Supplies the history each seed's dedupe check runs against.</param>
    /// <param name="writer">Performs the writes.</param>
    /// <param name="document">The changelog to draw highlights from.</param>
    /// <param name="lastActiveVersion">The version this app instance last ran, or <see langword="null"/> on a fresh install.</param>
    /// <param name="currentVersion">The version running now.</param>
    /// <param name="appVersionId">
    /// The <c>System_AppVersion</c> row for <paramref name="currentVersion"/>. Stamped on every
    /// notification written here — note that a catch-up run writes several notifications about
    /// *different* releases, all of them written *by* this one version, which is exactly the
    /// distinction provenance draws.
    /// </param>
    internal static async Task SeedAsync(
        INotificationReader reader, INotificationWriter writer, ChangelogDocument? document,
        string? lastActiveVersion, string currentVersion, Guid? appVersionId,
        IApiLocalizer? localizer = null,
        IReadOnlyDictionary<string, ChangelogDocument>? translatedDocuments = null)
    {
        foreach (Seed seed in BuildSeeds(document, lastActiveVersion, currentVersion, localizer, translatedDocuments))
        {
            await NotificationSeeding.SeedOnceAsync(
                reader, writer, NotificationType.Information, seed.Metadata,
                body:         seed.Body,
                title:        seed.Title,
                appVersionId: appVersionId,
                translations: seed.Translations);
        }
    }

    /// <summary>
    /// Loads the changelog document for each non-English language, dropping any the changelog does not
    /// actually have content for (#319).
    /// <para>
    /// <see cref="IChangelogReader.GetDocumentAsync"/> falls back to English rather than returning
    /// nothing, so the returned document's own <see cref="ChangelogDocument.Language"/> is what decides
    /// whether a language is really present. Storing an unchecked result would write English text into
    /// a <c>nl</c> translation row, and the read path would then report it as a Dutch translation —
    /// strictly worse than having no row, which it reports honestly as untranslated.
    /// </para>
    /// </summary>
    /// <param name="changelog">Reader the documents are fetched through.</param>
    internal static async Task<Dictionary<string, ChangelogDocument>> LoadTranslatedDocumentsAsync(IChangelogReader changelog)
    {
        Dictionary<string, ChangelogDocument> documents = new(StringComparer.OrdinalIgnoreCase);

        foreach (string language in TranslatedLanguages)
        {
            ChangelogDocument? document = await changelog.GetDocumentAsync(language);
            if (document is null)
                continue;

            if (!string.Equals(document.Language, language, StringComparison.OrdinalIgnoreCase))
                continue; // the reader fell back to English — this language has no content of its own

            documents[language] = document;
        }

        return documents;
    }

    // The languages this project ships changelog content in, minus the original. A constant rather than
    // a discovered set because IChangelogReader resolves one requested language at a time and exposes
    // no way to enumerate what it holds; keep in step with data/changelog/changelog.*.json.
    private static readonly string[] TranslatedLanguages = ["nl", "de"];

    // unreleased has no version to key on, and — unlike a tagged release — its content can change
    // freely before it ships (highlights added, edited, or removed across a development session). A
    // fixed dedupe key would show it once, ever, and never again reflect a later edit; keying on a
    // hash of the flagged highlights themselves means it re-surfaces whenever that content actually
    // changes, and stays deduped (no restart spam) whenever it doesn't.
    private static Seed? BuildUnreleasedSeed(
        ChangelogUnreleased? unreleased,
        IApiLocalizer? localizer,
        IReadOnlyDictionary<string, ChangelogDocument>? translatedDocuments)
    {
        List<string> highlights = unreleased?.GetHighlightsFor(ChangelogReservedAudience.Notification) ?? [];
        if (highlights.Count == 0)
            return null;

        string body = string.Join('\n', highlights);
        return new Seed(
            new WhatsNewMetadataDto
            {
                ReleaseState = NotificationReleaseState.Unreleased,
                ContentHash  = NotificationContentHash.Of(body),
            },
            Title: TitleFor(localizer, ApiMessages.NotificationWhatsNewUnreleasedTitle),
            Body:  body,
            Translations: BuildTranslations(
                localizer, translatedDocuments,
                ApiMessages.NotificationWhatsNewUnreleasedTitle, [],
                doc => doc.Unreleased?.GetHighlightsFor(ChangelogReservedAudience.Notification)));
    }

    // The title is not in the changelog — only the highlights are — so it comes from i18ntext/UI.*.json
    // like every other user-facing string. A null localizer is the unit-test path, where the English
    // template is enough and loading the locale files would prove nothing.
    private static string TitleFor(IApiLocalizer? localizer, string key, params object[] args)
        => localizer is null
            ? ApiLocalizerFormatting.Substitute(EnglishTitleFallback(key), args)
            : NotificationTranslations.Original(localizer, key, args);

    private static string EnglishTitleFallback(string key) => key switch
    {
        ApiMessages.NotificationWhatsNewReleasedTitle   => "What's new in v{0}",
        ApiMessages.NotificationWhatsNewUnreleasedTitle => "What's new (unreleased)",
        _                                              => key,
    };

    /// <summary>
    /// One translation per language whose changelog genuinely carries this entry's flagged highlights
    /// (#319). The body comes from that language's own changelog document — already translated at
    /// source, so nothing is translated here; the title comes from <c>UI.*.json</c>, which is the only
    /// half the changelog has no answer for.
    /// <para>
    /// A language contributes nothing unless its document actually has highlights for this entry.
    /// <c>IChangelogReader</c> falls back to English for a language it has no content for, so a caller
    /// that stored whatever came back would persist English text labelled <c>nl</c> — indistinguishable
    /// from a real Dutch translation, and worse than no row at all, which the read path reports
    /// honestly as untranslated.
    /// </para>
    /// </summary>
    private static IReadOnlyList<NotificationTranslation> BuildTranslations(
        IApiLocalizer? localizer,
        IReadOnlyDictionary<string, ChangelogDocument>? translatedDocuments,
        string titleKey,
        object[] titleArgs,
        Func<ChangelogDocument, List<string>?> highlightsFor)
    {
        if (localizer is null || translatedDocuments is null || translatedDocuments.Count == 0)
            return [];

        IReadOnlyDictionary<string, string> titles = localizer.ForEveryLanguage(titleKey, titleArgs);
        List<NotificationTranslation> translations = [];

        foreach (KeyValuePair<string, ChangelogDocument> entry in translatedDocuments)
        {
            if (string.Equals(entry.Key, NotificationTranslations.OriginalLanguage, StringComparison.OrdinalIgnoreCase))
                continue;

            List<string>? highlights = highlightsFor(entry.Value);
            if (highlights is null || highlights.Count == 0)
                continue;

            if (!titles.TryGetValue(entry.Key, out string? title))
                continue;

            translations.Add(new NotificationTranslation(entry.Key, title, string.Join('\n', highlights)));
        }

        return translations;
    }

    private static int IndexOfVersion(IReadOnlyList<ChangelogRelease> releases, string version)
    {
        for (int i = 0; i < releases.Count; i++)
        {
            if (releases[i].Version == version)
                return i;
        }

        return -1;
    }
}

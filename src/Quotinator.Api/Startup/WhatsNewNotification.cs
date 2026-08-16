using Quotinator.Changelog.Enums;
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
    internal readonly record struct Seed(WhatsNewMetadataDto Metadata, string Title, string Body);

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
    internal static List<Seed> BuildSeeds(ChangelogDocument? document, string? lastActiveVersion, string currentVersion)
    {
        if (document is null)
            return [];

        List<Seed> seeds = [];

        Seed? unreleasedSeed = BuildUnreleasedSeed(document.Unreleased);
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
                Title: $"What's new in v{release.Version}",
                Body:  string.Join('\n', highlights)));
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
        string? lastActiveVersion, string currentVersion, Guid? appVersionId)
    {
        foreach (Seed seed in BuildSeeds(document, lastActiveVersion, currentVersion))
        {
            await NotificationSeeding.SeedOnceAsync(
                reader, writer, NotificationType.Information, seed.Metadata,
                body:         seed.Body,
                title:        seed.Title,
                appVersionId: appVersionId);
        }
    }

    // unreleased has no version to key on, and — unlike a tagged release — its content can change
    // freely before it ships (highlights added, edited, or removed across a development session). A
    // fixed dedupe key would show it once, ever, and never again reflect a later edit; keying on a
    // hash of the flagged highlights themselves means it re-surfaces whenever that content actually
    // changes, and stays deduped (no restart spam) whenever it doesn't.
    private static Seed? BuildUnreleasedSeed(ChangelogUnreleased? unreleased)
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
            Title: "What's new (unreleased)",
            Body:  body);
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

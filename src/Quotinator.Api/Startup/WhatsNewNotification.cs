using System.Security.Cryptography;
using System.Text;
using Quotinator.Changelog.Enums;
using Quotinator.Changelog.Models;
using Quotinator.Data.Enums;
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
    /// <summary>A dedupe key and message ready to hand to <see cref="NotificationSeeding.SeedOnceAsync"/>.</summary>
    internal readonly record struct Seed(string DedupeKey, string Message);

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

            // Bare version numbers are not safe as a dedupe key on their own — "1.9.1" is a substring
            // of "1.9.10", so SeedOnceAsync's Contains-based dedupe check would risk a false-positive
            // match between two different patch versions whose digits happen to nest. The colon on
            // both sides makes the key unambiguous, and the message includes that exact bracketed form
            // verbatim.
            string dedupeKey = $"WhatsNew:v{release.Version}:";
            string message = $"{dedupeKey} What's new in v{release.Version}:\n" + string.Join('\n', highlights);
            seeds.Add(new Seed(dedupeKey, message));
        }

        return seeds;
    }

    /// <summary>Writes one notification per seed returned by <see cref="BuildSeeds"/>, skipping any already seeded.</summary>
    internal static async Task SeedAsync(
        INotificationReader reader, INotificationWriter writer, ChangelogDocument? document, string? lastActiveVersion, string currentVersion)
    {
        foreach (Seed seed in BuildSeeds(document, lastActiveVersion, currentVersion))
        {
            await NotificationSeeding.SeedOnceAsync(
                reader, writer, NotificationType.Information, seed.DedupeKey, seed.Message);
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

        string contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', highlights))))[..8];
        string dedupeKey = $"WhatsNew:unreleased:{contentHash}:";
        string message = $"{dedupeKey} What's new (unreleased):\n" + string.Join('\n', highlights);
        return new Seed(dedupeKey, message);
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

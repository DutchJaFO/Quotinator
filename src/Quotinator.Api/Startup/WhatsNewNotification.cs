using Quotinator.Changelog.Enums;
using Quotinator.Changelog.Models;
using Quotinator.Data.Enums;
using Quotinator.Data.Repositories;

namespace Quotinator.Api.Startup;

/// <summary>
/// Third producer for #278's notification mechanism (alongside #279's and #289's) — announces the
/// current release's notification-flagged changelog highlights (#307's
/// <c>ChangelogReservedAudience.Notification</c> convention) once per version. "Seen" state is the
/// existing server-side notification history itself (#278) — no separate cookie or <c>localStorage</c>
/// marker is needed.
/// </summary>
internal static class WhatsNewNotification
{
    /// <summary>A dedupe key and message ready to hand to <see cref="NotificationSeeding.SeedOnceAsync"/>.</summary>
    internal readonly record struct Seed(string DedupeKey, string Message);

    /// <summary>
    /// Builds the notification to write for <paramref name="currentVersion"/>, or <see langword="null"/>
    /// when there is nothing to report — no release in <paramref name="document"/> matches the running
    /// version, or the matching release has no notification-flagged highlights. Pure function of its
    /// inputs, kept separate from <see cref="SeedAsync"/> so it is unit-testable without a real
    /// <see cref="INotificationReader"/>/<see cref="INotificationWriter"/>.
    /// </summary>
    internal static Seed? BuildSeed(ChangelogDocument? document, string currentVersion)
    {
        ChangelogRelease? release = document?.Releases.FirstOrDefault(r => r.Version == currentVersion);
        List<string> highlights = release?.GetHighlightsFor(ChangelogReservedAudience.Notification) ?? [];
        if (highlights.Count == 0)
            return null;

        // Bare version numbers are not safe as a dedupe key on their own — "1.9.1" is a substring of
        // "1.9.10", so SeedOnceAsync's Contains-based dedupe check would risk a false-positive match
        // between two different patch versions whose digits happen to nest. The colon on both sides
        // makes the key unambiguous, and the message includes that exact bracketed form verbatim.
        string dedupeKey = $"WhatsNew:v{release!.Version}:";
        string message = $"{dedupeKey} What's new in v{release.Version}:\n" + string.Join('\n', highlights);
        return new Seed(dedupeKey, message);
    }

    /// <summary>Writes the what's-new notification for <paramref name="currentVersion"/>, if there is one, unless it was already seeded.</summary>
    internal static async Task SeedAsync(
        INotificationReader reader, INotificationWriter writer, ChangelogDocument? document, string currentVersion)
    {
        Seed? seed = BuildSeed(document, currentVersion);
        if (seed is null)
            return;

        await NotificationSeeding.SeedOnceAsync(
            reader, writer, NotificationType.Information, seed.Value.DedupeKey, seed.Value.Message);
    }
}

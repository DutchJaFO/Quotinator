using Quotinator.Api.Startup;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Changelog.Models;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Models;

namespace Quotinator.Api.Tests.Startup;

/// <summary>Exercises <see cref="WhatsNewNotification"/> (#81 — the third producer for #278's notification mechanism).</summary>
[TestClass]
public class WhatsNewNotificationTests
{
    private static NotificationEntity BuildExisting(string message) => new()
    {
        Type    = new SafeValue<NotificationType?>(nameof(NotificationType.Information), NotificationType.Information),
        Body    = message,
    };

    private static ChangelogRelease BuildRelease(string version, params string[] notificationHighlights) => new()
    {
        Version    = version,
        Date       = "2026-01-01",
        Highlights = ["An unflagged highlight, not notification-worthy"],
        AudienceHighlights = notificationHighlights.Length == 0
            ? []
            : new Dictionary<string, List<string>> { ["notification"] = [.. notificationHighlights] },
    };

    // Releases array is newest-first, matching ChangelogDocument.Releases' own documented contract.
    private static ChangelogDocument BuildDocument(params ChangelogRelease[] releasesNewestFirst) => new()
    {
        Language = "en",
        Releases = [.. releasesNewestFirst],
    };

    private static ChangelogDocument BuildDocument(string version, params string[] notificationHighlights) =>
        BuildDocument(BuildRelease(version, notificationHighlights));

    private static ChangelogUnreleased BuildUnreleased(params string[] notificationHighlights) => new()
    {
        Highlights = ["An unflagged unreleased highlight, not notification-worthy"],
        AudienceHighlights = notificationHighlights.Length == 0
            ? []
            : new Dictionary<string, List<string>> { ["notification"] = [.. notificationHighlights] },
    };

    [TestMethod]
    public async Task Seed_MatchingReleaseWithFlaggedHighlights_WritesInformationNotification()
    {
        var reader = new FakeNotificationReader();
        var writer = new FakeNotificationWriter();
        var document = BuildDocument("1.9.0", "A flagged highlight");

        await WhatsNewNotification.SeedAsync(reader, writer, document, lastActiveVersion: null, currentVersion: "1.9.0");

        Assert.HasCount(1, writer.WrittenMessages);
        var message = writer.WrittenMessages[0];
        Assert.Contains("WhatsNew:v1.9.0:", message);
        Assert.Contains("A flagged highlight", message);
        Assert.DoesNotContain("An unflagged highlight", message);
    }

    [TestMethod]
    public async Task Seed_NoMatchingRelease_WritesNothing()
    {
        var reader = new FakeNotificationReader();
        var writer = new FakeNotificationWriter();
        var document = BuildDocument("1.9.0", "A flagged highlight");

        await WhatsNewNotification.SeedAsync(reader, writer, document, lastActiveVersion: null, currentVersion: "2.0.0");

        Assert.IsEmpty(writer.WrittenMessages);
    }

    [TestMethod]
    public async Task Seed_MatchingReleaseNoFlaggedHighlights_WritesNothing()
    {
        var reader = new FakeNotificationReader();
        var writer = new FakeNotificationWriter();
        var document = BuildDocument("1.9.0");

        await WhatsNewNotification.SeedAsync(reader, writer, document, lastActiveVersion: null, currentVersion: "1.9.0");

        Assert.IsEmpty(writer.WrittenMessages);
    }

    [TestMethod]
    public async Task Seed_NestedVersionNumbers_DoNotFalselyDedupe()
    {
        var reader = new FakeNotificationReader();
        reader.Seed(BuildExisting("WhatsNew:v1.9.1: What's new in v1.9.1:\nAn earlier flagged highlight"));
        var writer = new FakeNotificationWriter();
        var document = BuildDocument(BuildRelease("1.9.10", "A newer flagged highlight"), BuildRelease("1.9.1", "An earlier flagged highlight"));

        await WhatsNewNotification.SeedAsync(reader, writer, document, lastActiveVersion: "1.9.1", currentVersion: "1.9.10");

        Assert.HasCount(1, writer.WrittenMessages, "v1.9.10 must not be falsely deduped against the existing v1.9.1 notification.");
        Assert.Contains("WhatsNew:v1.9.10:", writer.WrittenMessages[0]);
    }

    [TestMethod]
    public async Task Seed_AlreadySeededVersion_IsNoOp()
    {
        var reader = new FakeNotificationReader();
        reader.Seed(BuildExisting("WhatsNew:v1.9.0: What's new in v1.9.0:\nA flagged highlight"));
        var writer = new FakeNotificationWriter();
        var document = BuildDocument("1.9.0", "A flagged highlight");

        await WhatsNewNotification.SeedAsync(reader, writer, document, lastActiveVersion: "1.9.0", currentVersion: "1.9.0");

        Assert.IsEmpty(writer.WrittenMessages);
    }

    // ── Range/catch-up behaviour (multi-version upgrades) ──────────────────────────────────────

    /// <summary>A fresh install (no last active version recorded) sees only the current version, never the full backlog.</summary>
    [TestMethod]
    public void BuildSeeds_FreshInstall_OnlyConsidersCurrentVersion()
    {
        var document = BuildDocument(
            BuildRelease("1.8.3", "Newest flagged highlight"),
            BuildRelease("1.8.2", "Older flagged highlight"),
            BuildRelease("1.8.1", "Oldest flagged highlight"));

        var seeds = WhatsNewNotification.BuildSeeds(document, lastActiveVersion: null, currentVersion: "1.8.3");

        Assert.HasCount(1, seeds);
        Assert.Contains("WhatsNew:v1.8.3:", seeds[0].DedupeKey);
    }

    /// <summary>Upgrading across several versions at once catches up on every flagged release in between, one notification each.</summary>
    [TestMethod]
    public void BuildSeeds_UpgradeAcrossMultipleVersions_ReturnsOneSeedPerFlaggedReleaseInRange()
    {
        var document = BuildDocument(
            BuildRelease("1.8.3", "v1.8.3 flagged highlight"),
            BuildRelease("1.8.2", "v1.8.2 flagged highlight"),
            BuildRelease("1.8.1"), // no flagged highlights — must be skipped, not error
            BuildRelease("1.2.0", "v1.2.0 flagged highlight — outside the range, must not appear"));

        var seeds = WhatsNewNotification.BuildSeeds(document, lastActiveVersion: "1.8.1", currentVersion: "1.8.3");

        Assert.HasCount(2, seeds);
        Assert.Contains(s => s.DedupeKey == "WhatsNew:v1.8.3:", seeds);
        Assert.Contains(s => s.DedupeKey == "WhatsNew:v1.8.2:", seeds);
    }

    /// <summary>Running the same version again (no upgrade) has nothing new to report.</summary>
    [TestMethod]
    public void BuildSeeds_SameVersionAsLastActive_ReturnsNothing()
    {
        var document = BuildDocument("1.8.3", "A flagged highlight");

        var seeds = WhatsNewNotification.BuildSeeds(document, lastActiveVersion: "1.8.3", currentVersion: "1.8.3");

        Assert.IsEmpty(seeds);
    }

    /// <summary>A downgrade (current version older than the last active one) reports nothing rather than walking backwards.</summary>
    [TestMethod]
    public void BuildSeeds_Downgrade_ReturnsNothing()
    {
        var document = BuildDocument(BuildRelease("1.8.3", "newer"), BuildRelease("1.8.2", "older"));

        var seeds = WhatsNewNotification.BuildSeeds(document, lastActiveVersion: "1.8.3", currentVersion: "1.8.2");

        Assert.IsEmpty(seeds);
    }

    /// <summary>A last-active version that predates the changelog's own history falls back to just the current version, rather than guessing how far to walk.</summary>
    [TestMethod]
    public void BuildSeeds_LastActiveVersionNotInChangelog_FallsBackToCurrentVersionOnly()
    {
        var document = BuildDocument(BuildRelease("1.8.3", "flagged"), BuildRelease("1.8.2", "also flagged"));

        var seeds = WhatsNewNotification.BuildSeeds(document, lastActiveVersion: "0.9.0", currentVersion: "1.8.3");

        Assert.HasCount(1, seeds);
        Assert.Contains("WhatsNew:v1.8.3:", seeds[0].DedupeKey);
    }

    /// <summary>The running version not existing in the changelog at all (e.g. a dev build) reports nothing.</summary>
    [TestMethod]
    public void BuildSeeds_CurrentVersionNotInChangelog_ReturnsNothing()
    {
        var document = BuildDocument("1.8.2", "flagged");

        var seeds = WhatsNewNotification.BuildSeeds(document, lastActiveVersion: "1.8.1", currentVersion: "1.9.0-dev");

        Assert.IsEmpty(seeds);
    }

    // ── unreleased handling ─────────────────────────────────────────────────────────────────────
    // unreleased has no version — per developer direction it's effectively "the current version",
    // always considered regardless of lastActiveVersion/currentVersion, since a real release never
    // carries an unreleased section of its own.

    /// <summary>An unreleased entry with flagged highlights always produces a seed, even with no other releases in range.</summary>
    [TestMethod]
    public void BuildSeeds_UnreleasedWithFlaggedHighlights_IncludesUnreleasedSeed()
    {
        var document = new ChangelogDocument
        {
            Language   = "en",
            Unreleased = BuildUnreleased("An unreleased flagged highlight"),
            Releases   = [],
        };

        var seeds = WhatsNewNotification.BuildSeeds(document, lastActiveVersion: "1.8.3", currentVersion: "1.8.3");

        Assert.HasCount(1, seeds);
        Assert.Contains("WhatsNew:unreleased:", seeds[0].DedupeKey);
        Assert.Contains("An unreleased flagged highlight", seeds[0].Message);
        Assert.DoesNotContain("An unflagged unreleased highlight", seeds[0].Message);
    }

    /// <summary>No unreleased entry, or one with no flagged highlights, produces no unreleased seed.</summary>
    [TestMethod]
    public void BuildSeeds_UnreleasedAbsentOrNoFlaggedHighlights_NoUnreleasedSeed()
    {
        var noUnreleased = new ChangelogDocument { Language = "en", Unreleased = null, Releases = [] };
        var emptyUnreleased = new ChangelogDocument { Language = "en", Unreleased = BuildUnreleased(), Releases = [] };

        Assert.IsEmpty(WhatsNewNotification.BuildSeeds(noUnreleased, lastActiveVersion: "1.8.3", currentVersion: "1.8.3"));
        Assert.IsEmpty(WhatsNewNotification.BuildSeeds(emptyUnreleased, lastActiveVersion: "1.8.3", currentVersion: "1.8.3"));
    }

    /// <summary>The unreleased seed and any in-range release seeds both appear together — unreleased isn't a substitute for the release walk.</summary>
    [TestMethod]
    public void BuildSeeds_UnreleasedAndReleaseBothFlagged_ReturnsBothSeeds()
    {
        var document = new ChangelogDocument
        {
            Language   = "en",
            Unreleased = BuildUnreleased("An unreleased flagged highlight"),
            Releases   = [BuildRelease("1.8.3", "A release flagged highlight")],
        };

        var seeds = WhatsNewNotification.BuildSeeds(document, lastActiveVersion: null, currentVersion: "1.8.3");

        Assert.HasCount(2, seeds);
        Assert.Contains(s => s.DedupeKey.StartsWith("WhatsNew:unreleased:", StringComparison.Ordinal), seeds);
        Assert.Contains(s => s.DedupeKey == "WhatsNew:v1.8.3:", seeds);
    }

    /// <summary>Identical unreleased content produces the same dedupe key every time — no restart spam while nothing has changed.</summary>
    [TestMethod]
    public void BuildSeeds_UnreleasedContentUnchanged_ProducesSameDedupeKey()
    {
        var documentA = new ChangelogDocument { Language = "en", Unreleased = BuildUnreleased("Same highlight"), Releases = [] };
        var documentB = new ChangelogDocument { Language = "en", Unreleased = BuildUnreleased("Same highlight"), Releases = [] };

        var seedsA = WhatsNewNotification.BuildSeeds(documentA, lastActiveVersion: "1.8.3", currentVersion: "1.8.3");
        var seedsB = WhatsNewNotification.BuildSeeds(documentB, lastActiveVersion: "1.8.3", currentVersion: "1.8.3");

        Assert.AreEqual(seedsA[0].DedupeKey, seedsB[0].DedupeKey);
    }

    /// <summary>Changed unreleased content produces a different dedupe key, so the edit surfaces as a new notification instead of staying deduped against the stale one.</summary>
    [TestMethod]
    public void BuildSeeds_UnreleasedContentChanges_ProducesDifferentDedupeKey()
    {
        var before = new ChangelogDocument { Language = "en", Unreleased = BuildUnreleased("Original highlight"), Releases = [] };
        var after = new ChangelogDocument { Language = "en", Unreleased = BuildUnreleased("Edited highlight"), Releases = [] };

        var seedsBefore = WhatsNewNotification.BuildSeeds(before, lastActiveVersion: "1.8.3", currentVersion: "1.8.3");
        var seedsAfter = WhatsNewNotification.BuildSeeds(after, lastActiveVersion: "1.8.3", currentVersion: "1.8.3");

        Assert.AreNotEqual(seedsBefore[0].DedupeKey, seedsAfter[0].DedupeKey);
    }

    /// <summary>End-to-end through SeedAsync: unchanged unreleased content is not re-seeded on a later restart.</summary>
    [TestMethod]
    public async Task Seed_UnreleasedAlreadySeededWithSameContent_IsNoOp()
    {
        var document = new ChangelogDocument { Language = "en", Unreleased = BuildUnreleased("A flagged highlight"), Releases = [] };
        var existingKey = WhatsNewNotification.BuildSeeds(document, lastActiveVersion: "1.8.3", currentVersion: "1.8.3")[0].DedupeKey;

        var reader = new FakeNotificationReader();
        reader.Seed(BuildExisting($"{existingKey} What's new (unreleased):\nA flagged highlight"));
        var writer = new FakeNotificationWriter();

        await WhatsNewNotification.SeedAsync(reader, writer, document, lastActiveVersion: "1.8.3", currentVersion: "1.8.3");

        Assert.IsEmpty(writer.WrittenMessages);
    }
}

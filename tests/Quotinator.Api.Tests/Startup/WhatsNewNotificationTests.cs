using System.Text.Json;
using Quotinator.Api.Startup;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Changelog.Models;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Models;
using Quotinator.Data.Notifications;

namespace Quotinator.Api.Tests.Startup;

/// <summary>Exercises <see cref="WhatsNewNotification"/> (#81 — the third producer for #278's notification mechanism).</summary>
[TestClass]
public class WhatsNewNotificationTests
{
    /// <summary>
    /// An already-seeded notification, stored the way a real one is: its payload serialized into
    /// <c>Metadata</c> with the matching <c>MetadataKind</c> discriminator, which is what lets the
    /// seeding check read it back without knowing which producer wrote it. Before #312 the identity
    /// lived in <c>Body</c> text — a test still seeding it there would pass against a broken check.
    /// </summary>
    private static NotificationEntity BuildExisting(WhatsNewMetadataDto metadata) => new()
    {
        Type         = new SafeValue<NotificationType?>(nameof(NotificationType.Information), NotificationType.Information),
        Body         = "a previously seeded what's-new notification",
        Metadata     = JsonSerializer.Serialize(metadata),
        MetadataKind = new SafeValue<NotificationMetadataKind?>(
            nameof(NotificationMetadataKind.WhatsNew), NotificationMetadataKind.WhatsNew),
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
        FakeNotificationReader reader = new FakeNotificationReader();
        FakeNotificationWriter writer = new FakeNotificationWriter();
        ChangelogDocument document = BuildDocument("1.9.0", "A flagged highlight");

        await WhatsNewNotification.SeedAsync(reader, writer, document, lastActiveVersion: null, currentVersion: "1.9.0");

        Assert.HasCount(1, writer.WrittenMessages);
        string message = writer.WrittenMessages[0];
        Assert.Contains("A flagged highlight", message);
        Assert.DoesNotContain("An unflagged highlight", message);
        Assert.DoesNotContain("WhatsNew:", message,
            "#312 moved the dedupe key into metadata — it must no longer appear in the user-visible body.");

        (string? metadata, NotificationMetadataKind? kind) = writer.WrittenMetadata[0];
        Assert.AreEqual(NotificationMetadataKind.WhatsNew, kind);
        Assert.IsNotNull(metadata);
        WhatsNewMetadataDto? payload = JsonSerializer.Deserialize<WhatsNewMetadataDto>(metadata);
        Assert.IsNotNull(payload);
        Assert.AreEqual("1.9.0", payload.Version, "The release the highlights are about belongs in metadata, not parsed back out of prose.");
        Assert.IsNull(payload.ContentHash, "A tagged release identifies itself by version; the content hash is the unreleased section's mechanism.");
    }

    [TestMethod]
    public async Task Seed_NoMatchingRelease_WritesNothing()
    {
        FakeNotificationReader reader = new FakeNotificationReader();
        FakeNotificationWriter writer = new FakeNotificationWriter();
        ChangelogDocument document = BuildDocument("1.9.0", "A flagged highlight");

        await WhatsNewNotification.SeedAsync(reader, writer, document, lastActiveVersion: null, currentVersion: "2.0.0");

        Assert.IsEmpty(writer.WrittenMessages);
    }

    [TestMethod]
    public async Task Seed_MatchingReleaseNoFlaggedHighlights_WritesNothing()
    {
        FakeNotificationReader reader = new FakeNotificationReader();
        FakeNotificationWriter writer = new FakeNotificationWriter();
        ChangelogDocument document = BuildDocument("1.9.0");

        await WhatsNewNotification.SeedAsync(reader, writer, document, lastActiveVersion: null, currentVersion: "1.9.0");

        Assert.IsEmpty(writer.WrittenMessages);
    }

    [TestMethod]
    public async Task Seed_NestedVersionNumbers_DoNotFalselyDedupe()
    {
        FakeNotificationReader reader = new FakeNotificationReader();
        reader.Seed(BuildExisting(new WhatsNewMetadataDto { Version = "1.9.1" }));
        FakeNotificationWriter writer = new FakeNotificationWriter();
        ChangelogDocument document = BuildDocument(BuildRelease("1.9.10", "A newer flagged highlight"), BuildRelease("1.9.1", "An earlier flagged highlight"));

        await WhatsNewNotification.SeedAsync(reader, writer, document, lastActiveVersion: "1.9.1", currentVersion: "1.9.10");

        Assert.HasCount(1, writer.WrittenMessages, "v1.9.10 must not be falsely deduped against the existing v1.9.1 notification.");
        WhatsNewMetadataDto? payload = JsonSerializer.Deserialize<WhatsNewMetadataDto>(writer.WrittenMetadata[0].Metadata!);
        Assert.AreEqual("1.9.10", payload!.Version,
            "Structural identity compares version values, so no substring relationship between \"1.9.1\" and \"1.9.10\" can exist.");
    }

    [TestMethod]
    public async Task Seed_AlreadySeededVersion_IsNoOp()
    {
        FakeNotificationReader reader = new FakeNotificationReader();
        reader.Seed(BuildExisting(new WhatsNewMetadataDto { Version = "1.9.0" }));
        FakeNotificationWriter writer = new FakeNotificationWriter();
        ChangelogDocument document = BuildDocument("1.9.0", "A flagged highlight");

        await WhatsNewNotification.SeedAsync(reader, writer, document, lastActiveVersion: "1.9.0", currentVersion: "1.9.0");

        Assert.IsEmpty(writer.WrittenMessages);
    }

    // ── Range/catch-up behaviour (multi-version upgrades) ──────────────────────────────────────

    /// <summary>A fresh install (no last active version recorded) sees only the current version, never the full backlog.</summary>
    [TestMethod]
    public void BuildSeeds_FreshInstall_OnlyConsidersCurrentVersion()
    {
        ChangelogDocument document = BuildDocument(
            BuildRelease("1.8.3", "Newest flagged highlight"),
            BuildRelease("1.8.2", "Older flagged highlight"),
            BuildRelease("1.8.1", "Oldest flagged highlight"));

        List<WhatsNewNotification.Seed> seeds = WhatsNewNotification.BuildSeeds(document, lastActiveVersion: null, currentVersion: "1.8.3");

        Assert.HasCount(1, seeds);
        Assert.AreEqual("1.8.3", seeds[0].Metadata.Version);
    }

    /// <summary>Upgrading across several versions at once catches up on every flagged release in between, one notification each.</summary>
    [TestMethod]
    public void BuildSeeds_UpgradeAcrossMultipleVersions_ReturnsOneSeedPerFlaggedReleaseInRange()
    {
        ChangelogDocument document = BuildDocument(
            BuildRelease("1.8.3", "v1.8.3 flagged highlight"),
            BuildRelease("1.8.2", "v1.8.2 flagged highlight"),
            BuildRelease("1.8.1"), // no flagged highlights — must be skipped, not error
            BuildRelease("1.2.0", "v1.2.0 flagged highlight — outside the range, must not appear"));

        List<WhatsNewNotification.Seed> seeds = WhatsNewNotification.BuildSeeds(document, lastActiveVersion: "1.8.1", currentVersion: "1.8.3");

        Assert.HasCount(2, seeds);
        Assert.Contains(s => s.Metadata.Version == "1.8.3", seeds);
        Assert.Contains(s => s.Metadata.Version == "1.8.2", seeds);
    }

    /// <summary>Running the same version again (no upgrade) has nothing new to report.</summary>
    [TestMethod]
    public void BuildSeeds_SameVersionAsLastActive_ReturnsNothing()
    {
        ChangelogDocument document = BuildDocument("1.8.3", "A flagged highlight");

        List<WhatsNewNotification.Seed> seeds = WhatsNewNotification.BuildSeeds(document, lastActiveVersion: "1.8.3", currentVersion: "1.8.3");

        Assert.IsEmpty(seeds);
    }

    /// <summary>A downgrade (current version older than the last active one) reports nothing rather than walking backwards.</summary>
    [TestMethod]
    public void BuildSeeds_Downgrade_ReturnsNothing()
    {
        ChangelogDocument document = BuildDocument(BuildRelease("1.8.3", "newer"), BuildRelease("1.8.2", "older"));

        List<WhatsNewNotification.Seed> seeds = WhatsNewNotification.BuildSeeds(document, lastActiveVersion: "1.8.3", currentVersion: "1.8.2");

        Assert.IsEmpty(seeds);
    }

    /// <summary>A last-active version that predates the changelog's own history falls back to just the current version, rather than guessing how far to walk.</summary>
    [TestMethod]
    public void BuildSeeds_LastActiveVersionNotInChangelog_FallsBackToCurrentVersionOnly()
    {
        ChangelogDocument document = BuildDocument(BuildRelease("1.8.3", "flagged"), BuildRelease("1.8.2", "also flagged"));

        List<WhatsNewNotification.Seed> seeds = WhatsNewNotification.BuildSeeds(document, lastActiveVersion: "0.9.0", currentVersion: "1.8.3");

        Assert.HasCount(1, seeds);
        Assert.AreEqual("1.8.3", seeds[0].Metadata.Version);
    }

    /// <summary>The running version not existing in the changelog at all (e.g. a dev build) reports nothing.</summary>
    [TestMethod]
    public void BuildSeeds_CurrentVersionNotInChangelog_ReturnsNothing()
    {
        ChangelogDocument document = BuildDocument("1.8.2", "flagged");

        List<WhatsNewNotification.Seed> seeds = WhatsNewNotification.BuildSeeds(document, lastActiveVersion: "1.8.1", currentVersion: "1.9.0-dev");

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
        ChangelogDocument document = new ChangelogDocument
        {
            Language   = "en",
            Unreleased = BuildUnreleased("An unreleased flagged highlight"),
            Releases   = [],
        };

        List<WhatsNewNotification.Seed> seeds = WhatsNewNotification.BuildSeeds(document, lastActiveVersion: "1.8.3", currentVersion: "1.8.3");

        Assert.HasCount(1, seeds);
        Assert.IsNull(seeds[0].Metadata.Version, "The unreleased section has no version number to report.");
        Assert.IsNotNull(seeds[0].Metadata.ContentHash, "With no version, the unreleased section is identified by its content instead.");
        Assert.Contains("An unreleased flagged highlight", seeds[0].Body);
        Assert.DoesNotContain("An unflagged unreleased highlight", seeds[0].Body);
        Assert.DoesNotContain("WhatsNew:", seeds[0].Body,
            "#312 moved the dedupe key into metadata — it must no longer be smuggled into the user-visible body.");
    }

    /// <summary>No unreleased entry, or one with no flagged highlights, produces no unreleased seed.</summary>
    [TestMethod]
    public void BuildSeeds_UnreleasedAbsentOrNoFlaggedHighlights_NoUnreleasedSeed()
    {
        ChangelogDocument noUnreleased = new ChangelogDocument { Language = "en", Unreleased = null, Releases = [] };
        ChangelogDocument emptyUnreleased = new ChangelogDocument { Language = "en", Unreleased = BuildUnreleased(), Releases = [] };

        Assert.IsEmpty(WhatsNewNotification.BuildSeeds(noUnreleased, lastActiveVersion: "1.8.3", currentVersion: "1.8.3"));
        Assert.IsEmpty(WhatsNewNotification.BuildSeeds(emptyUnreleased, lastActiveVersion: "1.8.3", currentVersion: "1.8.3"));
    }

    /// <summary>The unreleased seed and any in-range release seeds both appear together — unreleased isn't a substitute for the release walk.</summary>
    [TestMethod]
    public void BuildSeeds_UnreleasedAndReleaseBothFlagged_ReturnsBothSeeds()
    {
        ChangelogDocument document = new ChangelogDocument
        {
            Language   = "en",
            Unreleased = BuildUnreleased("An unreleased flagged highlight"),
            Releases   = [BuildRelease("1.8.3", "A release flagged highlight")],
        };

        List<WhatsNewNotification.Seed> seeds = WhatsNewNotification.BuildSeeds(document, lastActiveVersion: null, currentVersion: "1.8.3");

        Assert.HasCount(2, seeds);
        Assert.Contains(s => s.Metadata.Version is null && s.Metadata.ContentHash is not null, seeds);
        Assert.Contains(s => s.Metadata.Version == "1.8.3", seeds);
    }

    /// <summary>Identical unreleased content identifies identically every time — no restart spam while nothing has changed.</summary>
    [TestMethod]
    public void BuildSeeds_UnreleasedContentUnchanged_ProducesSameIdentity()
    {
        ChangelogDocument documentA = new ChangelogDocument { Language = "en", Unreleased = BuildUnreleased("Same highlight"), Releases = [] };
        ChangelogDocument documentB = new ChangelogDocument { Language = "en", Unreleased = BuildUnreleased("Same highlight"), Releases = [] };

        List<WhatsNewNotification.Seed> seedsA = WhatsNewNotification.BuildSeeds(documentA, lastActiveVersion: "1.8.3", currentVersion: "1.8.3");
        List<WhatsNewNotification.Seed> seedsB = WhatsNewNotification.BuildSeeds(documentB, lastActiveVersion: "1.8.3", currentVersion: "1.8.3");

        Assert.IsTrue(seedsA[0].Metadata.IsSameNotificationAs(seedsB[0].Metadata));
    }

    /// <summary>Changed unreleased content identifies differently, so the edit surfaces as a new notification instead of staying deduped against the stale one.</summary>
    [TestMethod]
    public void BuildSeeds_UnreleasedContentChanges_ProducesDifferentIdentity()
    {
        ChangelogDocument before = new ChangelogDocument { Language = "en", Unreleased = BuildUnreleased("Original highlight"), Releases = [] };
        ChangelogDocument after = new ChangelogDocument { Language = "en", Unreleased = BuildUnreleased("Edited highlight"), Releases = [] };

        List<WhatsNewNotification.Seed> seedsBefore = WhatsNewNotification.BuildSeeds(before, lastActiveVersion: "1.8.3", currentVersion: "1.8.3");
        List<WhatsNewNotification.Seed> seedsAfter = WhatsNewNotification.BuildSeeds(after, lastActiveVersion: "1.8.3", currentVersion: "1.8.3");

        Assert.IsFalse(seedsBefore[0].Metadata.IsSameNotificationAs(seedsAfter[0].Metadata));
    }

    /// <summary>End-to-end through SeedAsync: unchanged unreleased content is not re-seeded on a later restart.</summary>
    [TestMethod]
    public async Task Seed_UnreleasedAlreadySeededWithSameContent_IsNoOp()
    {
        ChangelogDocument document = new ChangelogDocument { Language = "en", Unreleased = BuildUnreleased("A flagged highlight"), Releases = [] };
        WhatsNewMetadataDto existing = WhatsNewNotification.BuildSeeds(document, lastActiveVersion: "1.8.3", currentVersion: "1.8.3")[0].Metadata;

        FakeNotificationReader reader = new FakeNotificationReader();
        reader.Seed(BuildExisting(existing));
        FakeNotificationWriter writer = new FakeNotificationWriter();

        await WhatsNewNotification.SeedAsync(reader, writer, document, lastActiveVersion: "1.8.3", currentVersion: "1.8.3");

        Assert.IsEmpty(writer.WrittenMessages);
    }
}

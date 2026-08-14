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
        Message = message,
    };

    private static ChangelogDocument BuildDocument(string version, params string[] notificationHighlights) => new()
    {
        Language = "en",
        Releases =
        [
            new ChangelogRelease
            {
                Version    = version,
                Date       = "2026-01-01",
                Highlights = ["An unflagged highlight, not notification-worthy"],
                AudienceHighlights = notificationHighlights.Length == 0
                    ? []
                    : new Dictionary<string, List<string>> { ["notification"] = [.. notificationHighlights] },
            },
        ],
    };

    [TestMethod]
    public async Task Seed_MatchingReleaseWithFlaggedHighlights_WritesInformationNotification()
    {
        var reader = new FakeNotificationReader();
        var writer = new FakeNotificationWriter();
        var document = BuildDocument("1.9.0", "A flagged highlight");

        await WhatsNewNotification.SeedAsync(reader, writer, document, currentVersion: "1.9.0");

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

        await WhatsNewNotification.SeedAsync(reader, writer, document, currentVersion: "2.0.0");

        Assert.IsEmpty(writer.WrittenMessages);
    }

    [TestMethod]
    public async Task Seed_MatchingReleaseNoFlaggedHighlights_WritesNothing()
    {
        var reader = new FakeNotificationReader();
        var writer = new FakeNotificationWriter();
        var document = BuildDocument("1.9.0");

        await WhatsNewNotification.SeedAsync(reader, writer, document, currentVersion: "1.9.0");

        Assert.IsEmpty(writer.WrittenMessages);
    }

    [TestMethod]
    public async Task Seed_NestedVersionNumbers_DoNotFalselyDedupe()
    {
        var reader = new FakeNotificationReader();
        reader.Seed(BuildExisting("WhatsNew:v1.9.1: What's new in v1.9.1:\nAn earlier flagged highlight"));
        var writer = new FakeNotificationWriter();
        var document = BuildDocument("1.9.10", "A newer flagged highlight");

        await WhatsNewNotification.SeedAsync(reader, writer, document, currentVersion: "1.9.10");

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

        await WhatsNewNotification.SeedAsync(reader, writer, document, currentVersion: "1.9.0");

        Assert.IsEmpty(writer.WrittenMessages);
    }
}

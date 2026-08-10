using Quotinator.Api.Startup;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Models;

namespace Quotinator.Api.Tests.Startup;

/// <summary>Exercises <see cref="NotificationSeeding"/> (#279 — the first concrete producer for #278's notification mechanism).</summary>
[TestClass]
public class NotificationSeedingTests
{
    private static NotificationEntity BuildExisting(string message) => new()
    {
        Type    = new SafeValue<NotificationType?>(nameof(NotificationType.Warning), NotificationType.Warning),
        Message = message,
    };

    [TestMethod]
    public async Task SeedOnceAsync_NoExistingMatch_Writes()
    {
        var reader = new FakeNotificationReader();
        reader.Seed(BuildExisting("some unrelated notification"));
        var writer = new FakeNotificationWriter();

        await NotificationSeeding.SeedOnceAsync(
            reader, writer, NotificationType.Warning, dedupeKey: "GetAllImportBatches",
            message: "mentions GetAllImportBatches here");

        Assert.HasCount(1, writer.WrittenMessages);
        Assert.AreEqual("mentions GetAllImportBatches here", writer.WrittenMessages[0]);
    }

    [TestMethod]
    public async Task SeedOnceAsync_MatchingNotificationAlreadyExists_DoesNotWriteAgain()
    {
        var reader = new FakeNotificationReader();
        reader.Seed(BuildExisting("already mentions GetAllImportBatches from a prior startup"));
        var writer = new FakeNotificationWriter();

        await NotificationSeeding.SeedOnceAsync(
            reader, writer, NotificationType.Warning, dedupeKey: "GetAllImportBatches",
            message: "mentions GetAllImportBatches here");

        Assert.IsEmpty(writer.WrittenMessages);
    }

    [TestMethod]
    public async Task SeedOnceAsync_EmptyHistory_Writes()
    {
        var reader = new FakeNotificationReader();
        var writer = new FakeNotificationWriter();

        await NotificationSeeding.SeedOnceAsync(
            reader, writer, NotificationType.Information, dedupeKey: "some-key",
            message: "contains some-key");

        Assert.HasCount(1, writer.WrittenMessages);
    }
}

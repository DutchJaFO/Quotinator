using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Api.Services;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Notifications;
using Quotinator.Data.Repositories;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Api.Tests.Services;

/// <summary>
/// #348 step 31: the end result of a Reset or a reseed run from the application is recorded as a
/// notification, the lasting record of what the user was told. The text source resolves every key to
/// itself, so a title or body is asserted by the key it was written from.
/// </summary>
[TestClass]
public class DatabaseActionResultRecorderTests
{
    /// <summary>Each way a Reset or reseed can end, by name, so the data rows below can name them.</summary>
    private static (DatabaseAction Action, NotificationActionResult Result) Scenario(string name) => name switch
    {
        "ResetBackedUp"     => (DatabaseAction.Reset, NotificationActionResult.Success(RestorePointResult.BackedUp(BackupName))),
        "ResetSkipped"      => (DatabaseAction.Reset, NotificationActionResult.Success(RestorePointResult.Skipped(BackupOutcome.BudgetExceeded))),
        "ResetSetAside"     => (DatabaseAction.Reset, NotificationActionResult.Success(RestorePointResult.SetAside(SetAsideName))),
        "ResetRefused"      => (DatabaseAction.Reset, NotificationActionResult.RefusedForBackup(BackupOutcome.BudgetExceeded)),
        "ResetNotReplaced"  => (DatabaseAction.Reset, NotificationActionResult.RefusedForReset(ResetObstacle.DataDirectoryNotWritable)),
        "ResetNotIntact"    => (DatabaseAction.Reset, NotificationActionResult.ResetNotIntact("reason")),
        "ReseedBackedUp"    => (DatabaseAction.Reseed, NotificationActionResult.Success(RestorePointResult.BackedUp(BackupName))),
        "ReseedSkipped"     => (DatabaseAction.Reseed, NotificationActionResult.Success(RestorePointResult.Skipped(BackupOutcome.BudgetExceeded))),
        "ReseedRefused"     => (DatabaseAction.Reseed, NotificationActionResult.RefusedForBackup(BackupOutcome.BudgetExceeded)),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "no such scenario"),
    };

    private const string BackupName = "quotinatordata_backup.db";
    private const string SetAsideName = "quotinatordata_unreadable.db";

    /// <summary>A backup taken is a success; one skipped or a file set aside is a warning; a refusal or a mismatched schema is an error.</summary>
    [TestMethod]
    [DataRow("ResetBackedUp", NotificationType.Success)]
    [DataRow("ResetSkipped", NotificationType.Warning)]
    [DataRow("ResetSetAside", NotificationType.Warning)]
    [DataRow("ResetRefused", NotificationType.Error)]
    [DataRow("ResetNotReplaced", NotificationType.Error)]
    [DataRow("ResetNotIntact", NotificationType.Error)]
    [DataRow("ReseedBackedUp", NotificationType.Success)]
    [DataRow("ReseedSkipped", NotificationType.Warning)]
    [DataRow("ReseedRefused", NotificationType.Error)]
    public async Task RecordAsync_WritesTheTypeForHowTheActionEnded(string scenario, NotificationType expected)
    {
        FakeNotificationWriter writer = await RecordAsync(scenario);

        Assert.AreEqual(expected, Assert.ContainsSingle(writer.WrittenHeadings).Type);
    }

    [TestMethod]
    [DataRow("ResetBackedUp", NotificationMessageKeys.DatabaseResetCompletedTitle)]
    [DataRow("ResetSkipped", NotificationMessageKeys.DatabaseResetWithoutBackupTitle)]
    [DataRow("ResetSetAside", NotificationMessageKeys.DatabaseResetSetAsideTitle)]
    [DataRow("ResetRefused", NotificationMessageKeys.DatabaseResetRefusedTitle)]
    [DataRow("ResetNotReplaced", NotificationMessageKeys.DatabaseResetRefusedTitle)]
    [DataRow("ResetNotIntact", NotificationMessageKeys.DatabaseResetNotIntactTitle)]
    [DataRow("ReseedBackedUp", NotificationMessageKeys.DatabaseReseedCompletedTitle)]
    [DataRow("ReseedSkipped", NotificationMessageKeys.DatabaseReseedWithoutBackupTitle)]
    [DataRow("ReseedRefused", NotificationMessageKeys.DatabaseReseedRefusedTitle)]
    public async Task RecordAsync_WritesTheTitleForHowTheActionEnded(string scenario, string expectedKey)
    {
        FakeNotificationWriter writer = await RecordAsync(scenario);

        Assert.AreEqual(expectedKey, Assert.ContainsSingle(writer.WrittenHeadings).Title);
    }

    /// <summary>A skipped backup is said in the body as well as the title: that is what the user most needs to know.</summary>
    [TestMethod]
    [DataRow("ResetBackedUp", NotificationMessageKeys.DatabaseActionBackupTakenBody)]
    [DataRow("ResetSkipped", NotificationMessageKeys.DatabaseResetWithoutBackupBody)]
    [DataRow("ResetSetAside", NotificationMessageKeys.DatabaseResetSetAsideBody)]
    [DataRow("ResetRefused", NotificationMessageKeys.DatabaseActionRefusedForBackupBody)]
    [DataRow("ResetNotReplaced", NotificationMessageKeys.DatabaseResetRefusedForResetBody)]
    [DataRow("ResetNotIntact", NotificationMessageKeys.DatabaseResetNotIntactBody)]
    [DataRow("ReseedBackedUp", NotificationMessageKeys.DatabaseActionBackupTakenBody)]
    [DataRow("ReseedSkipped", NotificationMessageKeys.DatabaseReseedWithoutBackupBody)]
    [DataRow("ReseedRefused", NotificationMessageKeys.DatabaseActionRefusedForBackupBody)]
    public async Task RecordAsync_WritesTheBodyForHowTheActionEnded(string scenario, string expectedKey)
    {
        FakeNotificationWriter writer = await RecordAsync(scenario);

        Assert.AreEqual(expectedKey, Assert.ContainsSingle(writer.WrittenMessages));
    }

    [TestMethod]
    public async Task RecordAsync_WritesADatabaseActionResultNotification()
    {
        FakeNotificationWriter writer = await RecordAsync("ResetBackedUp");

        Assert.AreEqual(NotificationMetadataKind.DatabaseActionResult, Assert.ContainsSingle(writer.WrittenMetadata).Kind);
    }

    /// <summary>The payload names the backup, so the record says what to restore from.</summary>
    [TestMethod]
    public async Task RecordAsync_RecordsTheBackupItTook()
    {
        DatabaseActionResultMetadataDto payload = Payload(await RecordAsync("ReseedBackedUp"));

        Assert.AreEqual(BackupName, payload.FileName);
    }

    /// <summary>The payload names why no backup was taken, so a skipped one is never recorded without its reason.</summary>
    [TestMethod]
    public async Task RecordAsync_RecordsWhyTheBackupWasSkipped()
    {
        DatabaseActionResultMetadataDto payload = Payload(await RecordAsync("ResetSkipped"));

        Assert.AreEqual(BackupOutcome.BudgetExceeded, payload.BackupObstacle);
    }

    [TestMethod]
    public async Task RecordAsync_RecordsWhatRefusedTheReset()
    {
        DatabaseActionResultMetadataDto payload = Payload(await RecordAsync("ResetNotReplaced"));

        Assert.AreEqual(ResetObstacle.DataDirectoryNotWritable, payload.ResetObstacle);
    }

    /// <summary>Two runs ending the same way are two things that happened, so each is identified by its own run.</summary>
    [TestMethod]
    public async Task RecordAsync_TwoRunsWithTheSameResult_AreIdentifiedApart()
    {
        DatabaseActionResultMetadataDto first = Payload(await RecordAsync("ResetBackedUp"));
        DatabaseActionResultMetadataDto second = Payload(await RecordAsync("ResetBackedUp"));

        Assert.AreNotEqual(first.RunId, second.RunId);
    }

    /// <summary>The action's own result stands whether or not its record could be written.</summary>
    [TestMethod]
    public async Task RecordAsync_WhenTheWriteFails_DoesNotThrow()
    {
        (DatabaseAction action, NotificationActionResult result) = Scenario("ResetBackedUp");
        DatabaseActionResultRecorder recorder = new(
            new FakeNotificationReader(), new ThrowingNotificationWriter(), NoOpNotificationTextSource.Instance,
            NullLogger<DatabaseActionResultRecorder>.Instance);

        await recorder.RecordAsync(action, result);
    }

    private static async Task<FakeNotificationWriter> RecordAsync(string scenario)
    {
        (DatabaseAction action, NotificationActionResult result) = Scenario(scenario);
        FakeNotificationWriter writer = new();
        DatabaseActionResultRecorder recorder = new(
            new FakeNotificationReader(), writer, NoOpNotificationTextSource.Instance,
            NullLogger<DatabaseActionResultRecorder>.Instance);

        await recorder.RecordAsync(action, result);
        return writer;
    }

    private static DatabaseActionResultMetadataDto Payload(FakeNotificationWriter writer) =>
        (DatabaseActionResultMetadataDto)NotificationMetadataKinds.TryDeserialize(
            NotificationMetadataKind.DatabaseActionResult, Assert.ContainsSingle(writer.WrittenMetadata).Metadata)!;

    /// <summary>A writer that fails as a database that cannot be written does.</summary>
    private sealed class ThrowingNotificationWriter : INotificationWriter
    {
        public Task<NotificationEntity> WriteAsync(
            NotificationType type, string body, Guid? appVersionId, string? title = null, DateTime? expiresAt = null,
            NotificationDismissTrigger? dismissTrigger = null, string? metadata = null,
            NotificationMetadataKind? metadataKind = null, IReadOnlyList<NotificationTranslation>? translations = null)
            => throw new InvalidOperationException("the database cannot be written");

        public Task<NotificationEntity?> DismissAsync(Guid id, string? language = null) => throw new NotSupportedException();

        public Task<int> DismissByTriggerAndBatchAsync(NotificationDismissTrigger trigger, string batchId, NotificationDismissReason reason, NotificationResolution? resolution = null)
            => throw new NotSupportedException();

        public Task<int> DismissByTriggerAsync(NotificationDismissTrigger trigger, NotificationResolution? resolution = null)
            => throw new NotSupportedException();
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using Quotinator.Data.Enums;
using Quotinator.Data.Notifications;

namespace Quotinator.Data.Tests.Notifications;

/// <summary>
/// <see cref="NotificationConditionChecks"/> (#348): every registered check runs, so a kind added later is
/// re-checked everywhere without a caller changing.
/// </summary>
[TestClass]
public class NotificationConditionChecksTests
{
    [TestMethod]
    public async Task RunAsync_RunsEveryRegisteredCheck()
    {
        RecordingCheck first = new(NotificationMetadataKind.BackupQuotaReached, NotificationConditionOutcome.Raised);
        RecordingCheck second = new(NotificationMetadataKind.BackupRefused, NotificationConditionOutcome.Unchanged);

        await new NotificationConditionChecks([first, second], NullLogger<NotificationConditionChecks>.Instance).RunAsync();

        Assert.IsTrue(first.Ran && second.Ran, $"first ran: {first.Ran}, second ran: {second.Ran}");
    }

    [TestMethod]
    public async Task RunAsync_ReportsWhatEachCheckDid()
    {
        RecordingCheck first = new(NotificationMetadataKind.BackupQuotaReached, NotificationConditionOutcome.Raised);
        RecordingCheck second = new(NotificationMetadataKind.BackupRefused, NotificationConditionOutcome.Cleared);

        IReadOnlyList<NotificationConditionCheckResult> results = await new NotificationConditionChecks([first, second], NullLogger<NotificationConditionChecks>.Instance).RunAsync();

        NotificationConditionCheckResult[] expected =
        [
            new(NotificationMetadataKind.BackupQuotaReached, NotificationConditionOutcome.Raised),
            new(NotificationMetadataKind.BackupRefused, NotificationConditionOutcome.Cleared),
        ];

        Assert.AreSequenceEqual(expected, results);
    }

    /// <summary>Records that it ran, and reports a fixed outcome.</summary>
    private sealed class RecordingCheck(NotificationMetadataKind kind, NotificationConditionOutcome outcome) : INotificationConditionCheck
    {
        /// <summary>Whether <see cref="CheckAsync"/> was called.</summary>
        public bool Ran { get; private set; }

        /// <inheritdoc/>
        public NotificationMetadataKind Kind => kind;

        /// <inheritdoc/>
        public Task<NotificationConditionOutcome> CheckAsync()
        {
            Ran = true;
            return Task.FromResult(outcome);
        }
    }
}

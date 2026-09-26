using Quotinator.Api.Startup;
using Quotinator.Data.Enums;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// What an operator is told when a backup cannot be taken (#348): every obstacle names its own cause and a
/// remedy, and no obstacle is offered a remedy that cannot work for it. One statement per test.
/// </summary>
[TestClass]
public class BackupObstacleGuidanceTests
{
    private const string OverrideMarker = "allowNoBackup";
    private const string BackupRemovalRoute = "/api/v1/admin/backups";

    /// <summary>Every obstacle an attempt can report; <see cref="BackupOutcome.Succeeded"/> is not one.</summary>
    public static IEnumerable<object[]> Obstacles =>
        Enum.GetValues<BackupOutcome>().Where(o => o != BackupOutcome.Succeeded).Select(o => new object[] { o });

    /// <summary>Every obstacle other than <see cref="BackupOutcome.Unclassified"/>, which is the fallback itself.</summary>
    public static IEnumerable<object[]> RecognisedObstacles =>
        Obstacles.Where(o => (BackupOutcome)o[0] != BackupOutcome.Unclassified);

    [TestMethod]
    [DynamicData(nameof(Obstacles))]
    public void EveryObstacle_HasACause(BackupOutcome obstacle)
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(BackupObstacleGuidance.Cause(obstacle)));
    }

    [TestMethod]
    [DynamicData(nameof(Obstacles))]
    public void EveryObstacle_HasARemedy(BackupOutcome obstacle)
    {
        Assert.IsNotEmpty(BackupObstacleGuidance.Remedies(obstacle));
    }

    /// <summary>
    /// A recognised obstacle is described as itself, not as "a cause this build does not recognise": the
    /// point of naming each obstacle is that it is told apart from the fallback.
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(RecognisedObstacles))]
    public void RecognisedObstacle_IsNotDescribedAsTheUnrecognisedFallback(BackupOutcome obstacle)
    {
        Assert.AreNotEqual(BackupObstacleGuidance.Cause(BackupOutcome.Unclassified), BackupObstacleGuidance.Cause(obstacle));
    }

    /// <summary>A full quota can be overridden: the reset can complete without a backup.</summary>
    [TestMethod]
    public void BudgetExceeded_OffersTheOverride()
    {
        Assert.Contains(r => r.Contains(OverrideMarker, StringComparison.Ordinal), BackupObstacleGuidance.Remedies(BackupOutcome.BudgetExceeded));
    }

    /// <summary>A full quota is resolvable inside the application, and the remedy names the route that does it.</summary>
    [TestMethod]
    public void BudgetExceeded_OffersRemovingBackupsThroughTheApplication()
    {
        Assert.Contains(r => r.Contains(BackupRemovalRoute, StringComparison.Ordinal), BackupObstacleGuidance.Remedies(BackupOutcome.BudgetExceeded));
    }

    /// <summary>
    /// A database SQLite will not open cannot be dropped table by table either, so the override has nothing
    /// to proceed with. The first version of this guidance offered it anyway.
    /// </summary>
    [TestMethod]
    public void SourceUnreadable_DoesNotOfferTheOverride()
    {
        Assert.DoesNotContain(r => r.Contains(OverrideMarker, StringComparison.Ordinal), BackupObstacleGuidance.Remedies(BackupOutcome.SourceUnreadable));
    }

    /// <summary>Once the override has been tried and still refused, repeating it is advice the request disproved.</summary>
    [TestMethod]
    public void OverrideAlreadyTried_DoesNotRepeatTheOverride()
    {
        Assert.DoesNotContain(
            r => r.Contains(OverrideMarker, StringComparison.Ordinal),
            BackupObstacleGuidance.Remedies(BackupOutcome.BudgetExceeded, overrideAlreadyTried: true));
    }

    /// <summary>Withdrawing the disproved override leaves every other remedy in place.</summary>
    [TestMethod]
    public void OverrideAlreadyTried_KeepsTheOtherRemedies()
    {
        int offered = BackupObstacleGuidance.Remedies(BackupOutcome.BudgetExceeded).Count;

        Assert.HasCount(offered - 1, BackupObstacleGuidance.Remedies(BackupOutcome.BudgetExceeded, overrideAlreadyTried: true));
    }

    /// <summary>
    /// The override is a Reset parameter; a startup migration has none, so its reason must not offer one.
    /// The first version of this reason did, by reusing the Reset remedies whole.
    /// </summary>
    [TestMethod]
    public void MigrationRefusedReason_DoesNotOfferTheOverride()
    {
        Assert.DoesNotContain(OverrideMarker, BackupObstacleGuidance.MigrationRefusedReason(BackupOutcome.BudgetExceeded), StringComparison.Ordinal);
    }
}

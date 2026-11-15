using Quotinator.Api.Components.Controls;
using Quotinator.Api.Components.Pages;
using Quotinator.Api.Services;
using Quotinator.Api.Startup;
using Quotinator.Data.Database;
using Quotinator.Data.Enums;

namespace Quotinator.Api.Tests.Components;

/// <summary>
/// #348: what the Notifications page tells the user after an action ran. Tested through its static
/// helper, since this project has no bUnit; what it renders is a T2 concern.
/// </summary>
[TestClass]
public class NotificationsPageTests
{
    private static readonly Quotinator.Api.I18nText.UI Text = BaselineUiText.Load();

    /// <summary>
    /// The user agreed in a dialog to reseed without a backup; the page confirms that is what happened,
    /// from the action's own result, so a reseed with no restore point is never silent.
    /// </summary>
    [TestMethod]
    public void ReseedWithoutBackup_SaysThereIsNoRestorePoint()
    {
        string? outcome = Notifications.ActionOutcomeText(
            NotificationDismissTrigger.Reseed,
            NotificationActionResult.Success(RestorePointResult.Skipped(BackupOutcome.BudgetExceeded)), Text);

        Assert.AreEqual(
            Text.NotificationsActionRanWithoutBackup.Replace("{0}", nameof(BackupOutcome.BudgetExceeded), StringComparison.Ordinal),
            outcome);
    }

    /// <summary>
    /// #490: a Reset that set the file aside took no backup either, so the page says so, as it does for a
    /// skipped one, and says why: the file's name and the check layer that showed it was not a sound copy.
    /// </summary>
    [TestMethod]
    public void ResetThatSetTheFileAside_SaysWhyAndWhere()
    {
        string? outcome = Notifications.ActionOutcomeText(
            NotificationDismissTrigger.DatabaseReset,
            NotificationActionResult.Success(RestorePointResult.SetAside(SetAsideName, SetAsideReasonResult.FromCheck(ReasonFixtures.BrokenIndex))), Text);

        Assert.IsNotNull(outcome, "the page said nothing about a Reset that took no backup");
        Assert.Contains(SetAsideName, outcome, StringComparison.Ordinal);
        Assert.Contains(nameof(DatabaseCheckLayer.Indexes), outcome, StringComparison.Ordinal);
        Assert.Contains(nameof(DatabaseCheckLayerStatus.Failed), outcome, StringComparison.Ordinal);
        Assert.Contains(ReasonFixtures.BrokenIndex.Finding!, outcome, StringComparison.Ordinal);
    }

    /// <summary>#490: the same for a file only the backup found unreadable gives the backup's error.</summary>
    [TestMethod]
    public void ResetThatSetAsideAFileTheBackupFoundUnreadable_GivesTheBackupError()
    {
        string? outcome = Notifications.ActionOutcomeText(
            NotificationDismissTrigger.DatabaseReset,
            NotificationActionResult.Success(RestorePointResult.SetAside(SetAsideName, SetAsideReasonResult.FromBackup(ReasonFixtures.BackupError))), Text);

        Assert.IsNotNull(outcome, "the page said nothing about a Reset that took no backup");
        Assert.Contains(SetAsideName, outcome, StringComparison.Ordinal);
        Assert.Contains(ReasonFixtures.BackupError, outcome, StringComparison.Ordinal);
    }

    private const string SetAsideName = "quotinatordata_unreadable.db";

    /// <summary>An action that took its backup has nothing to warn about; the reloaded list is the answer.</summary>
    [TestMethod]
    public void ActionThatTookItsBackup_SaysNothing()
    {
        Assert.IsNull(Notifications.ActionOutcomeText(
            NotificationDismissTrigger.Reseed,
            NotificationActionResult.Success(RestorePointResult.BackedUp("backup.db")), Text));
    }

    /// <summary>
    /// #348 step 32: a reseed refused for want of a backup used to reload the list and say nothing. The
    /// page says which obstacle stopped it, in the reseed's own words rather than a Reset's.
    /// </summary>
    [TestMethod]
    public void RefusedReseed_NamesTheObstacleThatStoppedIt()
    {
        string? outcome = Notifications.ActionOutcomeText(
            NotificationDismissTrigger.Reseed, NotificationActionResult.RefusedForBackup(BackupOutcome.BudgetExceeded), Text);

        Assert.StartsWith(
            Text.NotificationsReseedRefusedForBackup.Replace("{0}", nameof(BackupOutcome.BudgetExceeded), StringComparison.Ordinal),
            outcome ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>Named, the obstacle is also explained: the cause is what the user acts on.</summary>
    [TestMethod]
    public void RefusedReseed_StatesTheObstaclesCause()
    {
        string? outcome = Notifications.ActionOutcomeText(
            NotificationDismissTrigger.Reseed, NotificationActionResult.RefusedForBackup(BackupOutcome.BudgetExceeded), Text);

        Assert.Contains(BackupObstacleGuidance.Cause(BackupOutcome.BudgetExceeded), outcome ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// A Reset run from this page and refused is told exactly as the repair control tells it, so the two
    /// surfaces that run the same action cannot describe its refusal differently.
    /// </summary>
    [TestMethod]
    public void RefusedReset_IsToldAsTheRepairControlTellsIt()
    {
        NotificationActionResult refused = NotificationActionResult.RefusedForBackup(BackupOutcome.BudgetExceeded);

        Assert.AreEqual(
            DatabaseRepairActions.RepairOutcomeText(refused, Text),
            Notifications.ActionOutcomeText(NotificationDismissTrigger.DatabaseReset, refused, Text));
    }
}

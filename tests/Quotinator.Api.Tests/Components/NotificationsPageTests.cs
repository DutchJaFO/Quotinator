using Quotinator.Api.Components.Pages;
using Quotinator.Api.Services;
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
            NotificationActionResult.Success(RestorePointResult.Skipped(BackupOutcome.BudgetExceeded)), Text);

        Assert.AreEqual(
            Text.NotificationsActionRanWithoutBackup.Replace("{0}", nameof(BackupOutcome.BudgetExceeded), StringComparison.Ordinal),
            outcome);
    }

    /// <summary>An action that took its backup has nothing to warn about; the reloaded list is the answer.</summary>
    [TestMethod]
    public void ActionThatTookItsBackup_SaysNothing()
    {
        Assert.IsNull(Notifications.ActionOutcomeText(
            NotificationActionResult.Success(RestorePointResult.BackedUp("backup.db")), Text));
    }
}

using Quotinator.Api.Components.Controls;
using Quotinator.Api.Services;
using Quotinator.Api.Startup;
using Quotinator.Data.Enums;

namespace Quotinator.Api.Tests.Components;

/// <summary>
/// #350: the repair a degraded application offers, rendered by the startup modal and by the degraded
/// pages an operator reaches after dismissing it. Tested through its static helper: this project has
/// no bUnit, so what the control decides is asserted here and what it renders in
/// <c>startup-and-degradation/06</c>.
/// </summary>
[TestClass]
public class DatabaseRepairActionsTests
{
    /// <summary>
    /// The English strings the control renders, read from the baseline translation file so a test
    /// asserts the text an operator actually sees rather than a copy of it.
    /// </summary>
    private static readonly Quotinator.Api.I18nText.UI Text = BaselineUiText.Load();

    /// <summary>
    /// #482: every outcome sentence comes from the translation files, so a Dutch or German operator is
    /// not told in English what the repair did. A key missing from the baseline would render as nothing.
    /// </summary>
    [TestMethod]
    public void EveryOutcomeSentence_ComesFromTheTranslationFiles()
    {
        string?[] outcomes =
        [
            DatabaseRepairActions.RepairOutcomeText(NotificationActionResult.RefusedForReset(ResetObstacle.SetAsideMoveFailed), Text),
            DatabaseRepairActions.RepairOutcomeText(NotificationActionResult.RefusedForBackup(BackupOutcome.BudgetExceeded), Text),
            DatabaseRepairActions.RepairOutcomeText(NotificationActionResult.ResetNotIntact("reason"), Text),
            DatabaseRepairActions.RepairOutcomeText(new NotificationActionResult { Succeeded = false }, Text),
        ];
        string[] templates =
        [
            Text.DatabaseRepairRefusedForReset.Replace("{0}", nameof(ResetObstacle.SetAsideMoveFailed), StringComparison.Ordinal),
            Text.DatabaseRepairRefusedForBackup.Replace("{0}", nameof(BackupOutcome.BudgetExceeded), StringComparison.Ordinal),
            Text.DatabaseRepairResetNotIntact,
            Text.DatabaseRepairNoReasonReported,
        ];

        for (int i = 0; i < outcomes.Length; i++)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(templates[i]), $"outcome {i} has no baseline text");
            Assert.IsNotNull(outcomes[i]);
            Assert.StartsWith(templates[i], outcomes[i]!, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A Reset that refused for want of a backup changed nothing, and the operator needs the obstacle
    /// named: this control is the only surface they have, since nothing read from the database is shown
    /// while it is untrusted.
    /// </summary>
    [TestMethod]
    public void RefusedRepair_NamesTheObstacleThatStoppedIt()
    {
        string? outcome = DatabaseRepairActions.RepairOutcomeText(
            NotificationActionResult.RefusedForBackup(BackupOutcome.BudgetExceeded), Text);

        Assert.IsNotNull(outcome, "a refusal that says nothing leaves the operator with a button that did nothing");
        Assert.Contains(
            BackupObstacleGuidance.Cause(BackupOutcome.BudgetExceeded), outcome, StringComparison.Ordinal,
            "the obstacle's own cause is what tells the operator which of the five refusals they have");
    }

    /// <summary>
    /// #305: a Reset that ran but left a schema that does not match is not a refused backup, and the
    /// operator is told what is still wrong rather than that nothing was changed.
    /// </summary>
    [TestMethod]
    public void ResetNotIntact_NamesWhatIsStillWrong()
    {
        string? outcome = DatabaseRepairActions.RepairOutcomeText(
            NotificationActionResult.ResetNotIntact("missing table System_Notification"), Text);

        Assert.IsNotNull(outcome);
        Assert.Contains("missing table System_Notification", outcome, StringComparison.Ordinal);
        Assert.DoesNotContain("no reason was reported", outcome, StringComparison.Ordinal);
    }

    /// <summary>
    /// #482: a Reset of an unreadable file that could not set it aside is not a backup refusal, and is told
    /// as what it is, with the remedy, rather than as a backup problem or a missing reason.
    /// </summary>
    [TestMethod]
    public void RefusedForReset_NamesTheResetObstacleAndItsRemedy()
    {
        string? outcome = DatabaseRepairActions.RepairOutcomeText(
            NotificationActionResult.RefusedForReset(ResetObstacle.DataDirectoryNotWritable), Text);

        Assert.IsNotNull(outcome);
        Assert.Contains(ResetObstacleGuidance.Cause(ResetObstacle.DataDirectoryNotWritable), outcome, StringComparison.Ordinal);
        Assert.Contains(ResetObstacleGuidance.Remedies(ResetObstacle.DataDirectoryNotWritable)[0], outcome, StringComparison.Ordinal);
        Assert.DoesNotContain("no backup could be taken", outcome, StringComparison.Ordinal);
    }

    /// <summary>A repair that ran leaves nothing to report: the application is healthy and reloads.</summary>
    [TestMethod]
    public void SuccessfulRepair_SaysNothing()
    {
        Assert.IsNull(DatabaseRepairActions.RepairOutcomeText(NotificationActionResult.Success(), Text));
    }
}

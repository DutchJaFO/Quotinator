using Quotinator.Api.Components.Controls;
using Quotinator.Api.Enums;
using Quotinator.Api.Services;
using Quotinator.Api.Startup;
using Quotinator.Data.Database;
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
            DatabaseRepairActions.RepairOutcomeText(NotificationActionResult.RefusedForReset(ResetObstacle.SetAsideMoveFailed, null), Text),
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
    /// #490 step 17: a resumed migration that ran leaves the application healthy, and the control disappears with
    /// the degradation, so there is nothing left to say.
    /// </summary>
    [TestMethod]
    public void MigrationResumed_SaysNothing()
    {
        Assert.IsNull(DatabaseRepairActions.MigrationOutcomeText(
            new MigrationResumeResult { Outcome = MigrationResumeOutcome.Resumed }, Text));
    }

    /// <summary>
    /// Still no room and no confirmation: the operator is told in their own language that nothing ran, which
    /// obstacle stopped it, and what it means.
    /// </summary>
    [TestMethod]
    public void MigrationRefusedForBackup_NamesTheObstacleInTheOperatorsLanguage()
    {
        string? outcome = DatabaseRepairActions.MigrationOutcomeText(
            new MigrationResumeResult { Outcome = MigrationResumeOutcome.RefusedForBackup, BackupObstacle = BackupOutcome.InsufficientDiskSpace }, Text);

        Assert.IsNotNull(outcome);
        Assert.StartsWith(Text.DatabaseRepairMigrationRefusedForBackup.Replace("{0}", nameof(BackupOutcome.InsufficientDiskSpace), StringComparison.Ordinal),
            outcome, StringComparison.Ordinal);
        Assert.Contains(BackupObstacleGuidance.Cause(BackupOutcome.InsufficientDiskSpace), outcome, StringComparison.Ordinal);
    }

    /// <summary>The migration ran but the database degraded for another reason: the operator is told what is still wrong.</summary>
    [TestMethod]
    public void MigrationStillDegraded_NamesWhatIsStillWrong()
    {
        string? outcome = DatabaseRepairActions.MigrationOutcomeText(
            new MigrationResumeResult { Outcome = MigrationResumeOutcome.StillDegraded, FailureReason = "schema mismatch" }, Text);

        Assert.IsNotNull(outcome);
        Assert.StartsWith(Text.DatabaseRepairMigrationStillDegraded, outcome, StringComparison.Ordinal);
        Assert.Contains("schema mismatch", outcome, StringComparison.Ordinal);
    }

    /// <summary>Nothing to resume, which the control cannot normally show, still says so rather than nothing.</summary>
    [TestMethod]
    public void NothingToResume_SaysSo()
    {
        Assert.AreEqual(Text.DatabaseRepairNothingToResume, DatabaseRepairActions.MigrationOutcomeText(
            new MigrationResumeResult { Outcome = MigrationResumeOutcome.NothingToResume }, Text));
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
            NotificationActionResult.RefusedForReset(ResetObstacle.DataDirectoryNotWritable, null), Text);

        Assert.IsNotNull(outcome);
        Assert.Contains(ResetObstacleGuidance.Cause(ResetObstacle.DataDirectoryNotWritable), outcome, StringComparison.Ordinal);
        Assert.Contains(ResetObstacleGuidance.Remedies(ResetObstacle.DataDirectoryNotWritable)[0], outcome, StringComparison.Ordinal);
        Assert.DoesNotContain("no backup could be taken", outcome, StringComparison.Ordinal);
    }

    /// <summary>
    /// #490: a repair refused because the file could not be set aside says why it needed setting aside: the
    /// check layer that showed it was not a sound copy, its status and its finding.
    /// </summary>
    [TestMethod]
    public void RefusedForAFileThatNeededSettingAside_SaysWhichLayerShowedIt()
    {
        string? outcome = DatabaseRepairActions.RepairOutcomeText(
            NotificationActionResult.RefusedForReset(ResetObstacle.SetAsideMoveFailed, SetAsideReasonResult.FromCheck(ReasonFixtures.BrokenIndex)), Text);

        Assert.IsNotNull(outcome);
        Assert.Contains(ResetObstacleGuidance.Cause(ResetObstacle.SetAsideMoveFailed), outcome, StringComparison.Ordinal);
        Assert.Contains(nameof(DatabaseCheckLayer.Indexes), outcome, StringComparison.Ordinal);
        Assert.Contains(nameof(DatabaseCheckLayerStatus.Failed), outcome, StringComparison.Ordinal);
        Assert.Contains(ReasonFixtures.BrokenIndex.Finding!, outcome, StringComparison.Ordinal);
    }

    /// <summary>#490: the same refusal for a file only the backup found unreadable gives the backup's error.</summary>
    [TestMethod]
    public void RefusedForAFileTheBackupFoundUnreadable_GivesTheBackupError()
    {
        string? outcome = DatabaseRepairActions.RepairOutcomeText(
            NotificationActionResult.RefusedForReset(ResetObstacle.SetAsideMoveFailed, SetAsideReasonResult.FromBackup(ReasonFixtures.BackupError)), Text);

        Assert.IsNotNull(outcome);
        Assert.Contains(ReasonFixtures.BackupError, outcome, StringComparison.Ordinal);
    }

    /// <summary>A repair that ran leaves nothing to report: the application is healthy and reloads.</summary>
    [TestMethod]
    public void SuccessfulRepair_SaysNothing()
    {
        Assert.IsNull(DatabaseRepairActions.RepairOutcomeText(NotificationActionResult.Success(), Text));
    }
}

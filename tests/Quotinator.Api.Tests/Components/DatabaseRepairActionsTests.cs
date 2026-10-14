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
    /// A Reset that refused for want of a backup changed nothing, and the operator needs the obstacle
    /// named: this control is the only surface they have, since nothing read from the database is shown
    /// while it is untrusted.
    /// </summary>
    [TestMethod]
    public void RefusedRepair_NamesTheObstacleThatStoppedIt()
    {
        string? outcome = DatabaseRepairActions.RepairOutcomeText(
            NotificationActionResult.RefusedForBackup(BackupOutcome.BudgetExceeded));

        Assert.IsNotNull(outcome, "a refusal that says nothing leaves the operator with a button that did nothing");
        Assert.Contains(
            BackupObstacleGuidance.Cause(BackupOutcome.BudgetExceeded), outcome, StringComparison.Ordinal,
            "the obstacle's own cause is what tells the operator which of the five refusals they have");
    }

    /// <summary>A repair that ran leaves nothing to report: the application is healthy and reloads.</summary>
    [TestMethod]
    public void SuccessfulRepair_SaysNothing()
    {
        Assert.IsNull(DatabaseRepairActions.RepairOutcomeText(NotificationActionResult.Success()));
    }
}

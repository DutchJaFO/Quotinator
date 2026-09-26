using Quotinator.Api.Components.Controls;
using Quotinator.Api.Startup;
using Quotinator.Constants.Api;
using Quotinator.Data.Enums;

namespace Quotinator.Api.Tests.Components;

/// <summary>
/// #348: the degraded startup popup links the Knowledgebase entry when the database degraded because a
/// migration could not be backed up. Tested through its static helper: this project has no bUnit.
/// </summary>
[TestClass]
public class StartupErrorModalTests
{
    [TestMethod]
    public void RefusedMigrationReason_LinksTheKnowledgebaseEntry()
    {
        Assert.AreEqual(KnowledgebaseLinks.NoBackupCouldBeTaken,
            StartupErrorModal.KnowledgebaseLinkIn(BackupObstacleGuidance.MigrationRefusedReason(BackupOutcome.BudgetExceeded)));
    }

    /// <summary>A reason that names no entry gets no link, rather than one to an entry about something else.</summary>
    [TestMethod]
    public void ReasonNamingNoEntry_LinksNothing()
    {
        Assert.IsNull(StartupErrorModal.KnowledgebaseLinkIn("The data directory cannot be written. Restore write access to the data directory and restart."));
    }

    [TestMethod]
    public void NoReason_LinksNothing()
    {
        Assert.IsNull(StartupErrorModal.KnowledgebaseLinkIn(null));
    }
}

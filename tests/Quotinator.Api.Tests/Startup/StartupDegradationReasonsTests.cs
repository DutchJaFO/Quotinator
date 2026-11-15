using Quotinator.Api.Startup;
using Quotinator.Data.Database;
using Quotinator.Data.Database.Checks;
using Quotinator.Data.Enums;
using Quotinator.Data.Schema;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// #490: choosing the reason startup degrades with is one mapping. The #348, #350 and #305 cases keep their
/// text and their order, and the database check (ADR 028) adds its own. One test per case, plus the order in
/// which they win.
/// </summary>
[TestClass]
public class StartupDegradationReasonsTests
{
    private const int DataVersion = 40;
    private const int AppVersion = 70;

    private static readonly SchemaIntegrityResult NotIntact = new()
    {
        Differences = [new SchemaDifference(SchemaDifferenceKind.Missing, SchemaObjectKind.Table, "System_Notification", null, null, null)],
    };

    [TestMethod]
    public void For_Success_IsNoReason()
    {
        Assert.IsNull(For(DatabaseOperationResult.Success()));
    }

    /// <summary>#348, unchanged: a refused migration degrades with the backup obstacle's own reason.</summary>
    [TestMethod]
    public void For_MigrationRefused_IsTheBackupObstaclesReason()
    {
        string? reason = For(DatabaseOperationResult.RefusedForBackup(BackupOutcome.InsufficientDiskSpace, BackupGuardedStep.Migration));

        Assert.AreEqual(BackupObstacleGuidance.MigrationRefusedReason(BackupOutcome.InsufficientDiskSpace), reason);
    }

    /// <summary>#350, unchanged: a recorded version ahead of this build degrades with the overshoot reason.</summary>
    [TestMethod]
    public void For_SchemaVersionOvershoot_IsTheOvershootReason()
    {
        string? reason = For(DatabaseOperationResult.Success(), overshoot: true);

        Assert.AreEqual(SchemaOvershootGuidance.Reason(DataVersion, AppVersion), reason);
    }

    /// <summary>#305, unchanged: a schema that does not match its migration path degrades naming the differences.</summary>
    [TestMethod]
    public void For_SchemaNotIntact_IsTheIntegrityReason()
    {
        string? reason = For(DatabaseOperationResult.NotIntact(), integrity: NotIntact);

        Assert.AreEqual(SchemaIntegrityGuidance.Reason(NotIntact), reason);
    }

    /// <summary>
    /// The Storage layer now detects an unwritable data directory before the database is opened, and keeps
    /// #326's reason, which says a Reset cannot help.
    /// </summary>
    [TestMethod]
    public void For_StorageFailed_IsTheDataDirectoryReason()
    {
        string? reason = For(DatabaseOperationResult.StoppedByCheck(CheckFailingAt(DatabaseCheckLayer.Storage, DatabaseCheckLayerStatus.Failed)));

        Assert.AreEqual(StartupDegradationReasons.DataDirectoryNotWritable, reason);
    }

    /// <summary>Decision D: a check that could not run soundly degrades with its own reason, never as a pass.</summary>
    [TestMethod]
    public void For_CheckInconclusive_IsTheInconclusiveReason()
    {
        string? reason = For(DatabaseOperationResult.StoppedByCheck(CheckFailingAt(DatabaseCheckLayer.Pages, DatabaseCheckLayerStatus.Inconclusive)));

        Assert.AreEqual(StartupDegradationReasons.CheckInconclusive, reason);
    }

    /// <summary>Layers Format to Indexes keep the generic reason until #457 gives each its own.</summary>
    /// <param name="layer">The layer that failed.</param>
    [TestMethod]
    [DataRow(DatabaseCheckLayer.Format)]
    [DataRow(DatabaseCheckLayer.Journal)]
    [DataRow(DatabaseCheckLayer.Pages)]
    [DataRow(DatabaseCheckLayer.Indexes)]
    public void For_FileLayerFailed_IsTheGenericReason(DatabaseCheckLayer layer)
    {
        string? reason = For(DatabaseOperationResult.StoppedByCheck(CheckFailingAt(layer, DatabaseCheckLayerStatus.Failed)));

        Assert.AreEqual(StartupDegradationReasons.InitialisationFailed, reason);
    }

    /// <summary>
    /// Decision E: until #493 makes enforcement explicit, a dangling reference may be the application's own,
    /// so a References failure does not stop the initialisation. It is carried on a successful result, and
    /// is no reason to degrade.
    /// </summary>
    [TestMethod]
    public void For_ReferencesFailed_IsNoReason()
    {
        DatabaseCheckResult check = CheckFailingAt(DatabaseCheckLayer.References, DatabaseCheckLayerStatus.Failed);

        Assert.AreEqual(DatabaseCheckLayer.References, check.FirstFailure?.Layer, "Positive control failed: the fixture does not fail at References.");
        Assert.IsNull(For(DatabaseOperationResult.Success(check: check)));
    }

    /// <summary>
    /// The reasons that a test above names are distinct from each other, so a case falling through to the
    /// generic reason by mistake cannot pass as another.
    /// </summary>
    [TestMethod]
    public void For_EveryNewReason_IsDistinct()
    {
        string[] reasons = [StartupDegradationReasons.DataDirectoryNotWritable, StartupDegradationReasons.InitialisationFailed, StartupDegradationReasons.CheckInconclusive];

        Assert.HasCount(reasons.Length, reasons.Distinct());
        Assert.IsNotNull(For(DatabaseOperationResult.StoppedByCheck(CheckFailingAt(DatabaseCheckLayer.Format, DatabaseCheckLayerStatus.Failed))),
            "Positive control failed: a failed check produced no reason at all.");
    }

    /// <summary>
    /// #348's precedence, unchanged: a refused migration leaves the schema behind this build, the stricter
    /// fault, so it wins over an overshoot reported in the same initialisation.
    /// </summary>
    [TestMethod]
    public void For_MigrationRefusedAndOvershoot_MigrationWins()
    {
        string? reason = For(DatabaseOperationResult.RefusedForBackup(BackupOutcome.InsufficientDiskSpace, BackupGuardedStep.Migration), overshoot: true);

        Assert.AreEqual(BackupObstacleGuidance.MigrationRefusedReason(BackupOutcome.InsufficientDiskSpace), reason);
    }

    /// <summary>#350's precedence, unchanged: an overshoot skips the schema comparison and is the more specific fault.</summary>
    [TestMethod]
    public void For_OvershootAndSchemaNotIntact_OvershootWins()
    {
        string? reason = For(DatabaseOperationResult.NotIntact(), overshoot: true, integrity: NotIntact);

        Assert.AreEqual(SchemaOvershootGuidance.Reason(DataVersion, AppVersion), reason);
    }

    /// <summary>
    /// A check that stopped the initialisation stopped it before anything else ran, so its reason wins over
    /// any other signal reported alongside it.
    /// </summary>
    [TestMethod]
    public void For_CheckStoppedAndOvershoot_CheckWins()
    {
        string? reason = For(
            DatabaseOperationResult.StoppedByCheck(CheckFailingAt(DatabaseCheckLayer.Storage, DatabaseCheckLayerStatus.Failed)),
            overshoot: true, integrity: NotIntact);

        Assert.AreEqual(StartupDegradationReasons.DataDirectoryNotWritable, reason);
    }

    private static string? For(DatabaseOperationResult initialisation, bool overshoot = false, SchemaIntegrityResult? integrity = null) =>
        StartupDegradationReasons.For(initialisation, overshoot, DataVersion, AppVersion, integrity);

    /// <summary>A check whose layers before <paramref name="failing"/> passed, and none after it ran.</summary>
    private static DatabaseCheckResult CheckFailingAt(DatabaseCheckLayer failing, DatabaseCheckLayerStatus status) =>
        new()
        {
            DatabaseName = "main",
            Depth        = DatabaseCheckDepth.Full,
            Layers       =
            [
                .. Enum.GetValues<DatabaseCheckLayer>().Select(layer => new DatabaseCheckLayerResult
                {
                    Layer  = layer,
                    Status = layer < failing ? DatabaseCheckLayerStatus.Passed : layer == failing ? status : DatabaseCheckLayerStatus.NotRun,
                }),
            ],
        };
}

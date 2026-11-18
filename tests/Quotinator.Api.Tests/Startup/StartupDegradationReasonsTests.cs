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

    /// <summary>
    /// #490 (developer, 2026-11-16): the schema is compared on an overshoot, so the reason no longer says its shape is
    /// unknown. It says what is still unknown, what the migrations this build never saw meant, and where the comparison
    /// is read.
    /// </summary>
    [TestMethod]
    public void OvershootReason_PointsToTheComparisonRatherThanCallingTheShapeUnknown()
    {
        string reason = SchemaOvershootGuidance.Reason(DataVersion, AppVersion);

        Assert.DoesNotContain("shape is unknown", reason, StringComparison.Ordinal);
        Assert.Contains(Quotinator.Constants.Routes.ApiRoutes.DatabaseIntegrity, reason, StringComparison.Ordinal);
    }

    /// <summary>The overshoot notification says the same, so the two surfaces do not contradict each other.</summary>
    [TestMethod]
    public void OvershootNotification_PointsToTheComparisonRatherThanCallingTheShapeUnknown()
    {
        string body = Quotinator.Api.Tests.Components.BaselineUiText.Load().NotificationSchemaOvershootBody;

        Assert.DoesNotContain("shape is unknown", body, StringComparison.Ordinal);
        Assert.Contains(Quotinator.Constants.Routes.ApiRoutes.DatabaseIntegrity, body, StringComparison.Ordinal);
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
        string? reason = For(DatabaseOperationResult.StoppedByCheck(CheckFailingAt(DatabaseCheckLayer.Storage, DatabaseCheckLayerStatus.Failed, DatabaseStorageFault.DirectoryNotWritable)));

        Assert.AreEqual(StartupDegradationReasons.DataDirectoryNotWritable, reason);
    }

    /// <summary>
    /// #457, decision E: a file that could not be opened is no fault of the directory, and may be intact, so it gets
    /// its own reason, neither the data directory's nor the generic one.
    /// </summary>
    [TestMethod]
    public void For_StorageFoundTheFileUnreadable_IsTheCouldNotBeOpenedReason()
    {
        DatabaseCheckResult check = CheckFailingAt(DatabaseCheckLayer.Storage, DatabaseCheckLayerStatus.Failed, DatabaseStorageFault.FileUnreadable);

        string? reason = For(DatabaseOperationResult.StoppedByCheck(check));

        Assert.AreNotEqual(StartupDegradationReasons.InitialisationFailed, reason);
        Assert.AreEqual(DatabaseUnsoundGuidance.Reason(check.FirstFailure!, null), reason);
    }

    /// <summary>A layer whose test ran and gave neither a pass nor a fail degrades with its own reason, never as a pass.</summary>
    [TestMethod]
    public void For_CheckInconclusive_IsTheInconclusiveReason()
    {
        string? reason = For(DatabaseOperationResult.StoppedByCheck(CheckFailingAt(DatabaseCheckLayer.Pages, DatabaseCheckLayerStatus.Inconclusive)));

        Assert.AreEqual(StartupDegradationReasons.CheckInconclusive, reason);
    }

    /// <summary>#457: layers Format to Indexes each degrade with the reason that names what is wrong with the file.</summary>
    /// <param name="layer">The layer that failed.</param>
    [TestMethod]
    [DataRow(DatabaseCheckLayer.Format)]
    [DataRow(DatabaseCheckLayer.Journal)]
    [DataRow(DatabaseCheckLayer.Pages)]
    [DataRow(DatabaseCheckLayer.Indexes)]
    public void For_FileLayerFailed_IsTheUnsoundReason(DatabaseCheckLayer layer)
    {
        DatabaseCheckResult check = CheckFailingAt(layer, DatabaseCheckLayerStatus.Failed);

        string? reason = For(DatabaseOperationResult.StoppedByCheck(check));

        Assert.AreNotEqual(StartupDegradationReasons.InitialisationFailed, reason);
        Assert.AreEqual(DatabaseUnsoundGuidance.Reason(check.FirstFailure!, null), reason);
    }

    /// <summary>#457, decision 3: what stops a Reset reaches the reason, so it does not offer one that would be refused.</summary>
    [TestMethod]
    public void For_FileLayerFailedWithAResetObstacle_PassesTheObstacleToTheReason()
    {
        DatabaseCheckResult check = CheckFailingAt(DatabaseCheckLayer.Pages, DatabaseCheckLayerStatus.Failed);

        string? reason = For(DatabaseOperationResult.StoppedByCheck(check), obstacle: ResetObstacle.SetAsideFolderNotWritable);

        Assert.AreEqual(DatabaseUnsoundGuidance.Reason(check.FirstFailure!, ResetObstacle.SetAsideFolderNotWritable), reason);
    }

    /// <summary>
    /// #457, decision D: asking whether a Reset could proceed writes a probe, so it is asked only for a failure whose
    /// reason offers that Reset, and once.
    /// </summary>
    [TestMethod]
    public void For_AsksResetReadinessOnlyForAReasonThatOffersTheReset()
    {
        int asked = 0;
        ResetObstacle? Readiness()
        {
            asked++;
            return null;
        }

        StartupDegradationReasons.For(DatabaseOperationResult.StoppedByCheck(CheckFailingAt(DatabaseCheckLayer.Storage, DatabaseCheckLayerStatus.Failed, DatabaseStorageFault.DirectoryNotWritable)),
            false, DataVersion, AppVersion, null, Readiness);
        StartupDegradationReasons.For(DatabaseOperationResult.StoppedByCheck(CheckFailingAt(DatabaseCheckLayer.Pages, DatabaseCheckLayerStatus.Inconclusive)),
            false, DataVersion, AppVersion, null, Readiness);
        Assert.AreEqual(0, asked, "Readiness was asked for a reason that does not offer the Reset.");

        StartupDegradationReasons.For(DatabaseOperationResult.StoppedByCheck(CheckFailingAt(DatabaseCheckLayer.Pages, DatabaseCheckLayerStatus.Failed)),
            false, DataVersion, AppVersion, null, Readiness);
        Assert.AreEqual(1, asked, "Readiness was not asked exactly once for a file that is not a sound copy.");
    }

    /// <summary>#457, decision D: a check after startup gives the same reason startup does, obstacle included.</summary>
    [TestMethod]
    public void ForCheck_FileLayerFailed_IsTheUnsoundReason()
    {
        DatabaseCheckResult check = CheckFailingAt(DatabaseCheckLayer.Indexes, DatabaseCheckLayerStatus.Failed);

        string? reason = StartupDegradationReasons.ForCheck(check, null, () => ResetObstacle.SetAsideFolderNotWritable);

        Assert.AreEqual(DatabaseUnsoundGuidance.Reason(check.FirstFailure!, ResetObstacle.SetAsideFolderNotWritable), reason);
    }

    /// <summary>#457, decision 4: the reason for what no layer foresaw no longer claims a schema-version mismatch.</summary>
    [TestMethod]
    public void InitialisationFailed_DoesNotClaimASchemaMismatch()
    {
        Assert.DoesNotContain("schema version", StartupDegradationReasons.InitialisationFailed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deleting the database file", StartupDegradationReasons.InitialisationFailed, StringComparison.Ordinal);
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
        string[] reasons =
        [
            StartupDegradationReasons.DataDirectoryNotWritable, StartupDegradationReasons.InitialisationFailed, StartupDegradationReasons.CheckInconclusive,
            DatabaseUnsoundGuidance.Reason(CheckFailingAt(DatabaseCheckLayer.Pages, DatabaseCheckLayerStatus.Failed).FirstFailure!, null),
            DatabaseUnsoundGuidance.Reason(CheckFailingAt(DatabaseCheckLayer.Storage, DatabaseCheckLayerStatus.Failed, DatabaseStorageFault.FileUnreadable).FirstFailure!, null),
        ];

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

    /// <summary>
    /// #350's precedence, unchanged: an overshoot is the more specific fault. Since #490 (developer, 2026-11-16) the
    /// schema is compared on an overshoot too, as its detail, never as a second reason.
    /// </summary>
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
            DatabaseOperationResult.StoppedByCheck(CheckFailingAt(DatabaseCheckLayer.Storage, DatabaseCheckLayerStatus.Failed, DatabaseStorageFault.DirectoryNotWritable)),
            overshoot: true, integrity: NotIntact);

        Assert.AreEqual(StartupDegradationReasons.DataDirectoryNotWritable, reason);
    }

    private static string? For(DatabaseOperationResult initialisation, bool overshoot = false, SchemaIntegrityResult? integrity = null, ResetObstacle? obstacle = null) =>
        StartupDegradationReasons.For(initialisation, overshoot, DataVersion, AppVersion, integrity, () => obstacle);

    /// <summary>A check whose layers before <paramref name="failing"/> passed, and none after it ran.</summary>
    private static DatabaseCheckResult CheckFailingAt(DatabaseCheckLayer failing, DatabaseCheckLayerStatus status, DatabaseStorageFault? fault = null) =>
        new()
        {
            DatabaseName = "main",
            Depth        = DatabaseCheckDepth.Full,
            Layers       =
            [
                .. Enum.GetValues<DatabaseCheckLayer>().Select(layer => new DatabaseCheckLayerResult
                {
                    Layer        = layer,
                    Status       = layer < failing ? DatabaseCheckLayerStatus.Passed : layer == failing ? status : DatabaseCheckLayerStatus.NotRun,
                    StorageFault = layer == failing ? fault : null,
                }),
            ],
        };
}

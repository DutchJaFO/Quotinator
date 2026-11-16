using Quotinator.Constants.Api;
using Quotinator.Constants.Routes;
using Quotinator.Data.Database;
using Quotinator.Data.Database.Checks;
using Quotinator.Data.Enums;
using Quotinator.Data.Schema;

namespace Quotinator.Api.Startup;

/// <summary>
/// The one mapping from how the main database's initialisation ended to the reason startup degrades with
/// (#490). It replaces the chain of <c>if</c> blocks in <c>Program.cs</c>, keeping the #348, #350 and #305
/// cases in their order and with their text, and adds the cases the database check (ADR 028) can produce.
/// <para>
/// First wins, as <see cref="DatabaseHealthState.MarkFailed"/> already is: the order of the cases is the
/// order of their precedence, and lives in one place.
/// </para>
/// </summary>
internal static class StartupDegradationReasons
{
    /// <summary>
    /// The reason for a data directory that cannot be written (#326), which the Storage layer now detects
    /// before the database is opened.
    /// </summary>
    internal const string DataDirectoryNotWritable =
        "The data directory cannot be written. This usually means the volume is mounted read-only, or " +
        "the container user lacks write permission on it. Restore write access to the data directory " +
        "and restart. A database Reset cannot resolve this: it writes too.";

    /// <summary>
    /// The generic reason, for a failure no more specific case describes, which includes a failure in the
    /// layers Format to Indexes.
    /// </summary>
    internal const string InitialisationFailed =
        "Database initialisation failed. This often means the database's recorded schema " +
        "version doesn't match its actual on-disk schema (e.g. after an interrupted upgrade). " +
        "Resolve with an explicit database Reset (POST " + ApiRoutes.DatabaseReset + ") or by " +
        "stopping the app, deleting the database file, and restarting.";

    /// <summary>
    /// The reason for a layer whose test ran and gave neither a pass nor a fail (ADR 028). An inconclusive
    /// layer is never a pass.
    /// </summary>
    internal const string CheckInconclusive =
        "The database check ran, but one of its tests gave no answer either way, so the database could not be " +
        "shown to be sound and is not used. GET " + ApiRoutes.DatabaseIntegrity + " names the test. Restart to " +
        "check it again; if the answer is the same, restore a backup or run a database Reset (POST " +
        ApiRoutes.DatabaseReset + ").";

    /// <summary>
    /// The reason startup degrades with, or <see langword="null"/> when it does not.
    /// </summary>
    /// <param name="initialisation">How the main database's initialisation ended.</param>
    /// <param name="schemaVersionOvershootDetected">Whether the recorded schema version is ahead of this build (#350).</param>
    /// <param name="dataSchemaVersion">The recorded version of Quotinator.Data's own migrations.</param>
    /// <param name="appSchemaVersion">The recorded version of this application's own migrations.</param>
    /// <param name="integrity">The schema comparison the initialisation ran (#305), when it ran one.</param>
    internal static string? For(
        DatabaseOperationResult initialisation,
        bool schemaVersionOvershootDetected,
        int dataSchemaVersion,
        int appSchemaVersion,
        SchemaIntegrityResult? integrity)
    {
        // #490: a check that stopped the initialisation stopped it before anything else ran, so it wins.
        if (StoppingLayer(initialisation) is { } stopping)
            return ForLayer(stopping);

        // #348: a refused migration leaves the schema behind this build, the stricter fault, so it wins over
        // an overshoot reported in the same initialisation.
        if (initialisation is { RefusedStep: BackupGuardedStep.Migration, BackupObstacle: BackupOutcome obstacle })
            return BackupObstacleGuidance.MigrationRefusedReason(obstacle);

        // #350: a recorded version ahead of this build is the more specific fault. Its schema is compared too (#490,
        // developer, 2026-11-16), and what that finds is the overshoot's detail, never a second reason.
        if (schemaVersionOvershootDetected)
            return SchemaOvershootGuidance.Reason(dataSchemaVersion, appSchemaVersion);

        // #305: the schema does not match the one its recorded version defines.
        if (initialisation.SchemaNotIntact && integrity is not null)
            return SchemaIntegrityGuidance.Reason(integrity);

        return null;
    }

    /// <summary>
    /// The reason an on-demand check degrades the application with (#490, developer, 2026-11-15: a failed
    /// integrity or storage check degrades the application at any point, not only at startup), or
    /// <see langword="null"/> when it does not. The same judgement as startup's: a failure that would stop
    /// startup, then a schema that is not intact.
    /// </summary>
    /// <param name="check">What the check found.</param>
    /// <param name="integrity">The schema comparison, when it could run.</param>
    internal static string? ForCheck(DatabaseCheckResult check, SchemaIntegrityResult? integrity)
    {
        if (check.DegradingFailure is { } failure)
            return ForLayer(failure);

        return integrity is { IsIntact: false } ? SchemaIntegrityGuidance.Reason(integrity) : null;
    }

    /// <summary>The reason for a layer that degrades, at startup and on demand alike.</summary>
    /// <param name="failure">The layer that failed or was inconclusive.</param>
    private static string ForLayer(DatabaseCheckLayerResult failure) =>
        failure.Status == DatabaseCheckLayerStatus.Inconclusive ? CheckInconclusive
            : failure.Layer != DatabaseCheckLayer.Storage ? InitialisationFailed
            : failure.StorageFault == DatabaseStorageFault.FileUnreadable ? InitialisationFailed
            : DataDirectoryNotWritable;

    /// <summary>
    /// The layer the database check stopped the initialisation at, or <see langword="null"/> when the check did
    /// not stop it: it passed, it found something that does not stop startup (decision E), or the initialisation
    /// stopped for another reason.
    /// </summary>
    private static DatabaseCheckLayerResult? StoppingLayer(DatabaseOperationResult initialisation) =>
        initialisation is { Succeeded: false, SchemaNotIntact: false, BackupObstacle: null, Check.FirstFailure: { } failure }
            ? failure
            : null;
}

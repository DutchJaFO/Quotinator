using Quotinator.Constants.Routes;
using Quotinator.Data.Database;
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
/// <para>
/// <b>A signature only:</b> <see cref="For"/> throws <see cref="NotImplementedException"/>.
/// <c>Program.cs</c> holds its own copy of <see cref="DataDirectoryNotWritable"/> and
/// <see cref="InitialisationFailed"/>.
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
    /// The reason for a check that could not run soundly (decision D): the database holds committed changes
    /// beside it that only a copy can check, and no copy could be made. An inconclusive check is never a pass.
    /// </summary>
    internal const string CheckInconclusive =
        "The database could not be checked. It holds changes that were not yet written into the main " +
        "file, which happens after an unclean stop, and checking them safely needs a working copy in the " +
        "data directory that could not be made. Free disk space in the data directory, or restore write " +
        "access to it, and restart: the database is then checked again.";

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
        SchemaIntegrityResult? integrity) =>
        throw new NotImplementedException();
}

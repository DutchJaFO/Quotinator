using Microsoft.Extensions.Logging;
using Quotinator.Data.Enums;
using Quotinator.Data.Logging;

namespace Quotinator.Data.Database.Checks;

/// <summary>
/// What startup does with a database check (#490, ADR 028), shared by every database's initializer so they
/// cannot judge the same result differently.
/// </summary>
internal static class StartupDatabaseCheck
{
    /// <summary>
    /// Whether the check stops startup before anything is written. Every failing or inconclusive layer does,
    /// except two. References does not until #493 makes enforcement explicit (decision E). Schema is judged by
    /// the initializer's own comparison after any migration (decision F): before migrating, an old database's
    /// schema differs from this build's by design.
    /// </summary>
    /// <param name="check">What the check found.</param>
    internal static bool Stops(DatabaseCheckResult check) =>
        check.FirstFailure is { } failure && failure.Layer is not (DatabaseCheckLayer.References or DatabaseCheckLayer.Schema);

    /// <summary>Logs what the check found: that it passed, or the layer it stopped at and why.</summary>
    /// <param name="logger">The initializer's logger.</param>
    /// <param name="check">What the check found.</param>
    internal static void Log(ILogger logger, DatabaseCheckResult check)
    {
        if (check.FirstFailure is not { } failure)
        {
            if (logger.IsEnabled(LogLevel.Information))
                logger.LogDatabaseCheckPassed(check.DatabaseName, check.Depth.ToString(), (long)check.Layers.Sum(l => l.Elapsed?.TotalMilliseconds ?? 0));
            return;
        }

        string finding = failure.Finding ?? string.Empty;
        if (failure.Status == DatabaseCheckLayerStatus.Inconclusive)
            logger.LogDatabaseCheckInconclusive(check.DatabaseName, failure.Layer.ToString(), finding);
        else if (failure.Layer == DatabaseCheckLayer.References)
            logger.LogDatabaseCheckDanglingReference(check.DatabaseName, finding);
        else
            logger.LogDatabaseCheckFailed(check.DatabaseName, failure.Layer.ToString(), finding);
    }
}

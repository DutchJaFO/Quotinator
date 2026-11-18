using Microsoft.Extensions.Logging;
using Quotinator.Data.Enums;
using Quotinator.Data.Paths;
using Quotinator.Logging;

namespace Quotinator.Data.Database;

/// <summary>
/// The one answer to whether a Reset could set the database file aside (#482, #457). <see cref="DatabaseInitializer"/>
/// asks it before a Reset moves anything, and a degraded reason asks it through <see cref="IResetReadiness"/>. It
/// depends only on where the database and the backups live, so it answers whichever initializer is registered.
/// </summary>
/// <param name="options">Where the database file and the backups folder are.</param>
/// <param name="logger">Logs the handled exception when the reserved folder cannot be created.</param>
public sealed class SetAsideReadiness(DatabaseOptions options, ILogger<SetAsideReadiness> logger) : IResetReadiness
{
    /// <inheritdoc/>
    public ResetObstacle? CheckSetAsideReadiness() => Check(options, logger);

    /// <summary>
    /// What would stop the file at <see cref="DatabaseOptions.DbPath"/> being set aside and replaced: the data
    /// directory, where the new database is built and placed, or the reserved folder the file moves into.
    /// </summary>
    /// <param name="options">Where the database file and the backups folder are.</param>
    /// <param name="logger">Logs the handled exception when the reserved folder cannot be created.</param>
    public static ResetObstacle? Check(DatabaseOptions options, ILogger logger)
    {
        if (!DirectoryWriteProbe.CanCreateFileIn(Path.GetDirectoryName(Path.GetFullPath(options.DbPath))!))
            return ResetObstacle.DataDirectoryNotWritable;

        string folder = Path.Combine(options.BackupsPath, BackupFileNames.UnreadableFolderName);
        try { Directory.CreateDirectory(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogExceptionHandled(ex, "the reserved folder for unreadable databases could not be created");
            return ResetObstacle.SetAsideFolderNotWritable;
        }

        return DirectoryWriteProbe.CanCreateFileIn(folder) ? null : ResetObstacle.SetAsideFolderNotWritable;
    }
}

using Microsoft.Extensions.Logging;
using Quotinator.Data.Enums;
using Quotinator.Logging;

namespace Quotinator.Data.Database.Checks;

/// <summary>
/// Storage (#490, ADR 028): whether the database file can be read, and whether its directory can be written,
/// which a migration, a backup and a Reset all need. A file that cannot be read stops the check. A directory
/// that cannot be written is reported, but it is no fault in the file, so the reading layers still run
/// (decision C).
/// </summary>
internal sealed class StorageLayer : IDatabaseCheckLayer
{
    /// <inheritdoc/>
    public DatabaseCheckLayer Layer => DatabaseCheckLayer.Storage;

    /// <inheritdoc/>
    public bool RunsAtFast => true;

    /// <inheritdoc/>
    public Task<DatabaseCheckLayerResult> RunAsync(SqliteCheckSubject subject)
    {
        if (subject.FileExists)
        {
            try
            {
                using FileStream _ = new(subject.DatabasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                subject.FileReadable = true;
            }
            // ADR 022: whether a file can be read is only known by opening it, so the exception is the answer.
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                subject.Logger.LogExceptionHandled(ex, "the database file could not be read");
                return Task.FromResult(LayerResults.Failed(Layer, $"The database file cannot be read: {ex.Message}"));
            }
        }

        if (!subject.CanCreateFileIn(subject.DataDirectory))
            return Task.FromResult(LayerResults.Failed(Layer, $"The data directory {subject.DataDirectory} cannot be written."));

        return Task.FromResult(subject.FileExists ? LayerResults.Passed(Layer) : LayerResults.NothingToCheck(Layer));
    }
}

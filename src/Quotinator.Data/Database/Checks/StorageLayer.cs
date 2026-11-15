using Microsoft.Extensions.Logging;
using Quotinator.Data.Enums;
using Quotinator.Logging;

namespace Quotinator.Data.Database.Checks;

/// <summary>
/// Storage (#490, ADR 028): whether the database file can be read, whether its directory can be written, which a
/// migration, a backup and a Reset all need, and whether there is room for what checking it needs. A file that
/// cannot be read stops the check. A directory that cannot be written is reported, but it is no fault in the
/// file, so the reading layers still run (decision C) unless they can run only on a copy.
/// <para>
/// When the <c>-wal</c> holds data, the reading layers run on a copy, and making it is this layer's: the room it
/// needs is measured first, and a copy that cannot be made is what Storage found, never an inconclusive layer
/// (developer, 2026-11-15).
/// </para>
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
                return Task.FromResult(LayerResults.StorageFailed(DatabaseStorageFault.FileUnreadable, $"The database file cannot be read: {ex.Message}"));
            }

            if (subject.CopyNeeded && CopyFor(subject) is { } copyFailure)
                return Task.FromResult(copyFailure);
        }

        if (!subject.CanCreateFileIn(subject.DataDirectory))
            return Task.FromResult(LayerResults.StorageFailed(DatabaseStorageFault.DirectoryNotWritable, $"The data directory {subject.DataDirectory} cannot be written."));

        return Task.FromResult(subject.FileExists ? LayerResults.Passed(Layer) : LayerResults.NothingToCheck(Layer));
    }

    /// <summary>
    /// Makes the copy the reading layers run on, once the room it needs is measured, or the failure that stops
    /// it: too little room, or a copy the folder or the operating system would not take. <see langword="null"/>
    /// when the copy was made.
    /// </summary>
    private static DatabaseCheckLayerResult? CopyFor(SqliteCheckSubject subject)
    {
        long needed = subject.CopyBytes;
        long free = subject.DiskSpace.GetAvailableFreeSpaceBytes(subject.DataDirectory);
        if (free < needed)
            return NoRoom(needed, free);

        if (subject.TryMakeCopy() is not { } whyNot)
            return null;

        // Measured again: something else may have taken the room since, or the need was underestimated.
        free = subject.DiskSpace.GetAvailableFreeSpaceBytes(subject.DataDirectory);
        return free < needed
            ? NoRoom(needed, free)
            : LayerResults.StorageFailed(DatabaseStorageFault.DirectoryNotWritable, whyNot);
    }

    private static DatabaseCheckLayerResult NoRoom(long needed, long free) =>
        LayerResults.StorageFailed(DatabaseStorageFault.InsufficientSpace,
            $"The -wal holds data, so the database can only be checked through a copy, which needs {needed} bytes; " +
            $"the data directory's volume has {free} free.");
}

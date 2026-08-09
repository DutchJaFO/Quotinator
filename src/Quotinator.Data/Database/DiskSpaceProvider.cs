namespace Quotinator.Data.Database;

/// <summary>Real <see cref="IDiskSpaceProvider"/> implementation backed by <see cref="DriveInfo"/>.</summary>
public sealed class DiskSpaceProvider : IDiskSpaceProvider
{
    /// <inheritdoc/>
    public long GetAvailableFreeSpaceBytes(string path)
        => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path)) ?? Path.GetFullPath(path)).AvailableFreeSpace;
}

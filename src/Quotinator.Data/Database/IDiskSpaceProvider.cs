namespace Quotinator.Data.Database;

/// <summary>Reports available disk space for a given path. Abstracted so <see cref="DatabaseInitializer"/>'s backup pre-flight check is unit-testable without a genuinely full disk.</summary>
public interface IDiskSpaceProvider
{
    /// <summary>Returns the number of free bytes available on the volume containing <paramref name="path"/>.</summary>
    /// <param name="path">A file or directory path on the volume to check.</param>
    long GetAvailableFreeSpaceBytes(string path);
}

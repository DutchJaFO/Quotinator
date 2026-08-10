using Quotinator.Data.Database;

namespace Quotinator.Data.Testing.NoOps;

/// <summary>No-op <see cref="IDiskSpaceProvider"/> for use in tests that do not exercise the backup storage pre-flight check (#277) — always reports plenty of free space.</summary>
public sealed class NoOpDiskSpaceProvider : IDiskSpaceProvider
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly NoOpDiskSpaceProvider Instance = new();

    /// <inheritdoc/>
    public long GetAvailableFreeSpaceBytes(string path) => long.MaxValue;
}

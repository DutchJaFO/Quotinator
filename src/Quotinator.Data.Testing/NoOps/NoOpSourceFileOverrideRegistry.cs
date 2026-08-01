using Quotinator.Data.Entities;
using Quotinator.Data.Import;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Testing.NoOps;

/// <summary>No-op <see cref="ISourceFileOverrideRegistry"/> for use in tests that do not exercise the rule-file override feature (#153) — nothing is ever registered, found, or removed.</summary>
public sealed class NoOpSourceFileOverrideRegistry : ISourceFileOverrideRegistry
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly NoOpSourceFileOverrideRegistry Instance = new();

    /// <inheritdoc/>
    public Task<SourceFileOverrideEntity?> FindAsync(string fileName, SeedBatchOrigin origin, CancellationToken cancellationToken = default)
        => Task.FromResult<SourceFileOverrideEntity?>(null);

    /// <inheritdoc/>
    public Task RegisterAsync(string fileName, SeedBatchOrigin origin, string contentHash, string? sourceBatchId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task<bool> RemoveAsync(string fileName, SeedBatchOrigin origin, CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}

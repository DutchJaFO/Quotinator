using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Testing.NoOps;

/// <summary>No-op <see cref="ISystemAuditReader"/> for use in unit tests that do not exercise audit read behaviour — always returns an empty page.</summary>
public sealed class NoOpSystemAuditReader : ISystemAuditReader
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly NoOpSystemAuditReader Instance = new();

    /// <inheritdoc/>
    public Task<SystemAuditPageResult> GetPagedAsync(string? table, string? recordId, int page, int pageSize)
        => Task.FromResult(new SystemAuditPageResult([], page, pageSize, 0));
}

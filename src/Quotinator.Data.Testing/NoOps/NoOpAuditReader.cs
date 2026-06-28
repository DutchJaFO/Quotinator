using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Testing.NoOps;

/// <summary>No-op <see cref="IAuditReader"/> for use in unit tests that do not exercise audit read behaviour — always returns an empty page.</summary>
public sealed class NoOpAuditReader : IAuditReader
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly NoOpAuditReader Instance = new();

    /// <inheritdoc/>
    public Task<AuditPageResult> GetPagedAsync(string? table, string? recordId, int page, int pageSize)
        => Task.FromResult(new AuditPageResult([], page, pageSize, 0));
}

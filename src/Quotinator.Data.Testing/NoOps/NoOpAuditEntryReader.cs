using Quotinator.Data.Entities;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Testing.NoOps;

/// <summary>No-op <see cref="IAuditEntryReader"/> for use in unit tests that do not exercise audit read behaviour — always returns an empty page.</summary>
public sealed class NoOpAuditEntryReader : IAuditEntryReader
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly NoOpAuditEntryReader Instance = new();

    /// <inheritdoc/>
    public Task<PagedItems<AuditEntryEntity>> GetPagedAsync(string? table, string? recordId, int page, int pageSize)
        => Task.FromResult(new PagedItems<AuditEntryEntity>([], page, pageSize, 0));
}

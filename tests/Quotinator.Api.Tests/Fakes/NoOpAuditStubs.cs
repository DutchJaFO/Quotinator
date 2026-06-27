using System.Data;
using Quotinator.Data.Entities;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>No-op audit writer for endpoint tests — never touches the database.</summary>
internal sealed class NoOpAuditWriter : IAuditWriter
{
    public Task WriteAsync(AuditEntry entry, IDbConnection connection, IDbTransaction? transaction = null)
        => Task.CompletedTask;

    public Task WriteAsync(AuditEntry entry)
        => Task.CompletedTask;

    public Task ClearAsync(string? table = null)
        => Task.CompletedTask;
}

/// <summary>No-op audit reader for endpoint tests — always returns an empty page.</summary>
internal sealed class NoOpAuditReader : IAuditReader
{
    public Task<AuditPageResult> GetPagedAsync(string? table, string? recordId, int page, int pageSize)
        => Task.FromResult(new AuditPageResult([], page, pageSize, 0));
}

/// <summary>No-op caller context for endpoint tests — agent is always null.</summary>
internal sealed class NoOpCallerContext : ICallerContext
{
    public string? Agent { get; set; }
}

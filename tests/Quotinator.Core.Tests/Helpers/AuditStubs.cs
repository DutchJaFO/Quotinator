using System.Data;
using Quotinator.Data.Entities;
using Quotinator.Data.Repositories;

namespace Quotinator.Core.Tests.Helpers;

/// <summary>No-op audit writer for tests that exercise repository contracts without requiring the AuditEntries table.</summary>
internal sealed class NoOpAuditWriter : IAuditWriter
{
    internal static readonly NoOpAuditWriter Instance = new();

    public Task WriteAsync(AuditEntry entry, IDbConnection connection, IDbTransaction? transaction = null)
        => Task.CompletedTask;

    public Task WriteAsync(AuditEntry entry)
        => Task.CompletedTask;

    public Task ClearAsync(string? table = null)
        => Task.CompletedTask;
}

/// <summary>No-op caller context for tests — agent is always null.</summary>
internal sealed class NoOpCallerContext : ICallerContext
{
    internal static readonly NoOpCallerContext Instance = new();
    public string? Agent { get; set; }
}

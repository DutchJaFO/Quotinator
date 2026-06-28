using System.Data;
using Quotinator.Data.Entities;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Testing.NoOps;

/// <summary>No-op <see cref="IAuditWriter"/> for use in unit tests that do not exercise audit behaviour.</summary>
public sealed class NoOpAuditWriter : IAuditWriter
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly NoOpAuditWriter Instance = new();

    /// <inheritdoc/>
    public Task WriteAsync(AuditEntry entry, IDbConnection connection, IDbTransaction? transaction = null)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task WriteAsync(AuditEntry entry)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task ClearAsync(string? table = null)
        => Task.CompletedTask;
}

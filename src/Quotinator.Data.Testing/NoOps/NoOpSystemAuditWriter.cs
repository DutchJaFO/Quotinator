using System.Data;
using Quotinator.Data.Entities;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Testing.NoOps;

/// <summary>No-op <see cref="ISystemAuditWriter"/> for use in unit tests that do not exercise audit behaviour.</summary>
public sealed class NoOpSystemAuditWriter : ISystemAuditWriter
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly NoOpSystemAuditWriter Instance = new();

    /// <inheritdoc/>
    public Task WriteAsync(SystemAuditEntry entry, IDbConnection connection, IDbTransaction? transaction = null)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task WriteAsync(IReadOnlyList<SystemAuditEntry> entries, IDbConnection connection, IDbTransaction? transaction = null)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task WriteAsync(SystemAuditEntry entry)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task ClearAsync(string? table = null)
        => Task.CompletedTask;
}

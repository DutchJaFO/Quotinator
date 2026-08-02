using System.Data;
using Quotinator.Data.Entities;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Testing.NoOps;

/// <summary>No-op <see cref="IAuditEntryWriter"/> for use in unit tests that do not exercise audit behaviour.</summary>
public sealed class NoOpAuditEntryWriter : IAuditEntryWriter
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly NoOpAuditEntryWriter Instance = new();

    /// <inheritdoc/>
    public Task WriteAsync(AuditEntryEntity entry, IDbConnection connection, IDbTransaction? transaction = null)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task WriteAsync(IReadOnlyList<AuditEntryEntity> entries, IDbConnection connection, IDbTransaction? transaction = null)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task WriteAsync(AuditEntryEntity entry)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task ClearAsync(string? table = null)
        => Task.CompletedTask;
}

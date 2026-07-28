using System.Data;
using Quotinator.Data.Entities;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Testing.NoOps;

/// <summary>No-op <see cref="ISystemChangeLogWriter"/> for use in unit tests that do not exercise change-logging behaviour.</summary>
public sealed class NoOpSystemChangeLogWriter : ISystemChangeLogWriter
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly NoOpSystemChangeLogWriter Instance = new();

    /// <inheritdoc/>
    public Task LogAsync(SystemChangeLog entry, IDbConnection connection, IDbTransaction? transaction = null)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task LogAsync(SystemChangeLog entry)
        => Task.CompletedTask;
}

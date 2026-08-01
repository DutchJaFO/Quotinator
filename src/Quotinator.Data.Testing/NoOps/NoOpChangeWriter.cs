using System.Data;
using Quotinator.Data.Entities;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Testing.NoOps;

/// <summary>No-op <see cref="IChangeWriter"/> for use in unit tests that do not exercise change-logging behaviour.</summary>
public sealed class NoOpChangeWriter : IChangeWriter
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly NoOpChangeWriter Instance = new();

    /// <inheritdoc/>
    public Task LogAsync(ChangeEntity entry, IDbConnection connection, IDbTransaction? transaction = null)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task LogAsync(ChangeEntity entry)
        => Task.CompletedTask;
}

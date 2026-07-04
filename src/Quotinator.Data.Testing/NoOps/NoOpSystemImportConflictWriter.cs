using System.Data;
using Quotinator.Data.Entities;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Testing.NoOps;

/// <summary>No-op <see cref="ISystemImportConflictWriter"/> for use in unit tests that do not exercise conflict-logging behaviour.</summary>
public sealed class NoOpSystemImportConflictWriter : ISystemImportConflictWriter
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly NoOpSystemImportConflictWriter Instance = new();

    /// <inheritdoc/>
    public Task WriteAsync(SystemImportConflict entry, IDbConnection connection, IDbTransaction? transaction = null)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task WriteAsync(SystemImportConflict entry)
        => Task.CompletedTask;
}

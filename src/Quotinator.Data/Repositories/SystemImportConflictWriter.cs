using System.Data;
using Dapper;
using Dapper.Contrib.Extensions;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Models;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Repositories;

/// <summary>
/// SQLite implementation of <see cref="ISystemImportConflictWriter"/>.
/// Extends <see cref="SqliteRepositoryBase{T}"/> directly — NOT <see cref="SqliteRepository{T}"/> —
/// so that writes do not trigger an audit write for a table this project doesn't audit.
/// Dapper.Contrib generates the INSERT statement from the <c>[Table]</c> and <c>[Key]</c>
/// attributes on <see cref="SystemImportConflict"/>; no SQL string is required for the initial write.
/// The status-transition methods (#149) use the raw <see cref="Sql.SystemImportConflicts"/> factory
/// methods instead, since Dapper.Contrib's <c>UpdateAsync</c> always rewrites every column.
/// </summary>
public sealed class SystemImportConflictWriter : SqliteRepositoryBase<SystemImportConflict>, ISystemImportConflictWriter
{
    /// <summary>Initialises the writer with the connection factory.</summary>
    public SystemImportConflictWriter(IDbConnectionFactory factory) : base(factory) { }

    /// <inheritdoc/>
    public async Task WriteAsync(SystemImportConflict entry, IDbConnection connection, IDbTransaction? transaction = null)
        => await connection.InsertAsync(entry, transaction);

    /// <inheritdoc/>
    public async Task WriteAsync(SystemImportConflict entry)
    {
        using var conn = Factory.CreateConnection();
        conn.Open();
        await conn.InsertAsync(entry);
    }

    /// <inheritdoc/>
    public async Task MarkDecidedAsync(Guid id, string decisionsJson, IDbConnection connection, IDbTransaction? transaction = null)
        => await connection.ExecuteAsync(
            Sql.SystemImportConflicts.MarkDecided,
            new
            {
                id,
                status       = ImportConflictStatus.Decided,
                mergedFields = decisionsJson,
                dateModified = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat),
            },
            transaction);

    /// <inheritdoc/>
    public async Task ClearDecisionAsync(Guid id, IDbConnection connection, IDbTransaction? transaction = null)
        => await connection.ExecuteAsync(
            Sql.SystemImportConflicts.ClearDecision,
            new
            {
                id,
                status       = ImportConflictStatus.Pending,
                dateModified = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat),
            },
            transaction);

    /// <inheritdoc/>
    public async Task MarkResolvedAsync(Guid id, IDbConnection connection, IDbTransaction? transaction = null)
    {
        var now = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat);
        await connection.ExecuteAsync(
            Sql.SystemImportConflicts.MarkResolved,
            new
            {
                id,
                status       = ImportConflictStatus.Resolved,
                resolvedAt   = now,
                dateModified = now,
            },
            transaction);
    }
}

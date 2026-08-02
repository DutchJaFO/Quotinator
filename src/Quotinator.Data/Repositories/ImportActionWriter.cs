using System.Data;
using Dapper;
using Dapper.Contrib.Extensions;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Models;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Repositories;

/// <summary>
/// SQLite implementation of <see cref="IImportActionWriter"/>.
/// Extends <see cref="SqliteRepositoryBase{T}"/> directly — NOT <see cref="SqliteRepository{T}"/> —
/// so that writes do not trigger an audit write for a table this project doesn't audit.
/// Dapper.Contrib generates the INSERT statement from the <c>[Table]</c> and <c>[Key]</c>
/// attributes on <see cref="ImportActionEntity"/>; no SQL string is required for the initial write.
/// The status-transition methods (#154) use the raw <see cref="Sql.SystemImportActions"/> factory
/// methods instead, since Dapper.Contrib's <c>UpdateAsync</c> always rewrites every column.
/// </summary>
public sealed class ImportActionWriter : SqliteRepositoryBase<ImportActionEntity>, IImportActionWriter
{
    /// <summary>Initialises the writer with the connection factory.</summary>
    public ImportActionWriter(IDbConnectionFactory factory) : base(factory) { }

    /// <inheritdoc/>
    public async Task WriteAsync(ImportActionEntity entry, IDbConnection connection, IDbTransaction? transaction = null)
        => await connection.InsertAsync(entry, transaction);

    /// <inheritdoc/>
    public async Task WriteAsync(ImportActionEntity entry)
    {
        using var conn = Factory.CreateConnection();
        conn.Open();
        await conn.InsertAsync(entry);
    }

    /// <inheritdoc/>
    public async Task WriteManyAsync(IEnumerable<ImportActionEntity> entries, IDbConnection connection, IDbTransaction? transaction = null)
        => await connection.InsertAsync(entries, transaction);

    /// <inheritdoc/>
    public async Task MarkDecidedAsync(Guid id, string decisionsJson, CompletenessStatus? markCompletenessAs, string? originalDecisionJson, IDbConnection connection, IDbTransaction? transaction = null)
        => await connection.ExecuteAsync(
            Sql.SystemImportActions.MarkDecided,
            new
            {
                id,
                status             = new SafeValue<ImportActionStatus?>(ImportActionStatus.Decided.ToString(), ImportActionStatus.Decided),
                mergedFields       = decisionsJson,
                markCompletenessAs = markCompletenessAs?.ToString(),
                originalDecision   = originalDecisionJson,
                dateModified       = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat),
            },
            transaction);

    /// <inheritdoc/>
    public async Task ClearDecisionAsync(Guid id, IDbConnection connection, IDbTransaction? transaction = null)
        => await connection.ExecuteAsync(
            Sql.SystemImportActions.ClearDecision,
            new
            {
                id,
                status       = new SafeValue<ImportActionStatus?>(ImportActionStatus.Pending.ToString(), ImportActionStatus.Pending),
                dateModified = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat),
            },
            transaction);

    /// <inheritdoc/>
    public async Task MarkAppliedAsync(Guid id, IDbConnection connection, IDbTransaction? transaction = null)
    {
        var now = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat);
        await connection.ExecuteAsync(
            Sql.SystemImportActions.MarkApplied,
            new
            {
                id,
                status       = new SafeValue<ImportActionStatus?>(ImportActionStatus.Applied.ToString(), ImportActionStatus.Applied),
                appliedAt    = now,
                dateModified = now,
            },
            transaction);
    }

    /// <inheritdoc/>
    public async Task MarkBatchDiscardedAsync(string batchId, IDbConnection connection, IDbTransaction? transaction = null)
    {
        var now = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat);
        await connection.ExecuteAsync(
            Sql.SystemImportActions.MarkBatchDiscarded,
            new
            {
                batchId,
                status       = new SafeValue<ImportActionStatus?>(ImportActionStatus.Discarded.ToString(), ImportActionStatus.Discarded),
                discardedAt  = now,
                dateModified = now,
            },
            transaction);
    }
}

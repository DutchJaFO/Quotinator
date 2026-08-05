using System.Data;
using Dapper;
using Dapper.Contrib.Extensions;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Repositories;

/// <summary>
/// SQLite implementation of <see cref="IAuditEntryWriter"/>.
/// Extends <see cref="SqliteRepositoryBase{T}"/> directly — NOT <see cref="SqliteRepository{T}"/> —
/// so that the INSERT does not trigger another audit write (infinite recursion).
/// Dapper.Contrib generates the INSERT statement from the <c>[Table]</c> attribute on
/// <see cref="AuditEntryEntity"/> and the <c>[ExplicitKey]</c> it inherits from
/// <see cref="Models.RecordBase"/>; no SQL string is required for writes.
/// </summary>
public sealed class AuditEntryWriter : SqliteRepositoryBase<AuditEntryEntity>, IAuditEntryWriter
{
    private readonly ICallerContext _callerContext;

    /// <summary>Initialises the writer with the connection factory and caller context.</summary>
    public AuditEntryWriter(IDbConnectionFactory factory, ICallerContext callerContext) : base(factory)
    {
        _callerContext = callerContext;
    }

    /// <inheritdoc/>
    public async Task WriteAsync(AuditEntryEntity entry, IDbConnection connection, IDbTransaction? transaction = null)
        => await connection.InsertAsync(entry, transaction);

    /// <inheritdoc/>
    public async Task WriteAsync(IReadOnlyList<AuditEntryEntity> entries, IDbConnection connection, IDbTransaction? transaction = null)
        => await connection.InsertAsync(entries, transaction);

    /// <inheritdoc/>
    public async Task WriteAsync(AuditEntryEntity entry)
    {
        using var conn = Factory.CreateConnection();
        conn.Open();
        await conn.InsertAsync(entry);
    }

    /// <inheritdoc/>
    public async Task ClearAsync(string? table = null)
    {
        using var conn = Factory.CreateConnection();
        conn.Open();

        if (table is not null)
        {
            // Scoped to one domain table's own audit entries — Audit_Change has no comparable
            // per-table scoping concept (its EntityType vocabulary doesn't map onto TableName), so a
            // scoped clear only ever clears what it explicitly names.
            await conn.ExecuteAsync(Sql.SystemAudit.DeleteByTable, new { table });
        }
        else
        {
            // #249: an unscoped clear empties the whole audit trail as one concern — both Audit_Entry
            // and Audit_Change — matching the export/date-range endpoints' own combined-tables model.
            await conn.ExecuteAsync(Sql.SystemAudit.DeleteAll);
            await conn.ExecuteAsync(Sql.SystemChangeLog.DeleteAll);
        }

        // Record the clear so there is always a trace that a purge occurred.
        await conn.InsertAsync(new AuditEntryEntity
        {
            TableName   = table ?? "Audit_Entry",
            Operation   = AuditOperation.Purge,
            Agent       = _callerContext.Agent,
            PerformedAt = DateTime.UtcNow,
        });
    }
}

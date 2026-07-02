using System.Data;
using Dapper;
using Dapper.Contrib.Extensions;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Repositories;

/// <summary>
/// SQLite implementation of <see cref="ISystemAuditWriter"/>.
/// Extends <see cref="SqliteRepositoryBase{T}"/> directly — NOT <see cref="SqliteRepository{T}"/> —
/// so that the INSERT does not trigger another audit write (infinite recursion).
/// Dapper.Contrib generates the INSERT statement from the <c>[Table]</c> and <c>[Key]</c>
/// attributes on <see cref="SystemAuditEntry"/>; no SQL string is required for writes.
/// </summary>
public sealed class SystemAuditWriter : SqliteRepositoryBase<SystemAuditEntry>, ISystemAuditWriter
{
    private readonly ICallerContext _callerContext;

    /// <summary>Initialises the writer with the connection factory and caller context.</summary>
    public SystemAuditWriter(IDbConnectionFactory factory, ICallerContext callerContext) : base(factory)
    {
        _callerContext = callerContext;
    }

    /// <inheritdoc/>
    public async Task WriteAsync(SystemAuditEntry entry, IDbConnection connection, IDbTransaction? transaction = null)
        => await connection.InsertAsync(entry, transaction);

    /// <inheritdoc/>
    public async Task WriteAsync(IReadOnlyList<SystemAuditEntry> entries, IDbConnection connection, IDbTransaction? transaction = null)
        => await connection.InsertAsync(entries, transaction);

    /// <inheritdoc/>
    public async Task WriteAsync(SystemAuditEntry entry)
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
            await conn.ExecuteAsync(Sql.SystemAudit.DeleteByTable, new { table });
        else
            await conn.ExecuteAsync(Sql.SystemAudit.DeleteAll);

        // Record the clear so there is always a trace that a purge occurred.
        await conn.InsertAsync(new SystemAuditEntry
        {
            TableName   = table ?? "System_AuditEntries",
            Operation   = AuditOperation.Purge,
            Agent       = _callerContext.Agent,
            PerformedAt = DateTime.UtcNow,
        });
    }
}

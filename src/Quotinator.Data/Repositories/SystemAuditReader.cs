using Dapper;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Models;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Repositories;

/// <summary>
/// SQLite implementation of <see cref="IAuditEntryReader"/>.
/// Extends <see cref="SqliteRepositoryBase{T}"/> directly — no audit writes are triggered by reads.
/// </summary>
public sealed class AuditEntryReader : SqliteRepositoryBase<AuditEntryEntity>, IAuditEntryReader
{
    /// <summary>Initialises the reader with the connection factory.</summary>
    public AuditEntryReader(IDbConnectionFactory factory) : base(factory) { }

    /// <inheritdoc/>
    public async Task<PagedItems<AuditEntryEntity>> GetPagedAsync(string? table, string? recordId, int page, int pageSize)
    {
        var filterTable    = table    is not null;
        var filterRecordId = recordId is not null;
        var limit           = pageSize == 0 ? -1 : pageSize;
        var offset          = pageSize == 0 ? 0  : (page - 1) * pageSize;

        using var conn = Factory.CreateConnection();
        conn.Open();

        var total = await conn.ExecuteScalarAsync<int>(
            Sql.SystemAudit.CountPaged(filterTable, filterRecordId),
            new { table, recordId });

        var items = (await conn.QueryAsync<AuditEntryEntity>(
            Sql.SystemAudit.SelectPaged(filterTable, filterRecordId),
            new { table, recordId, pageSize = limit, offset })).ToList();

        var effectivePageSize = pageSize == 0 ? items.Count : pageSize;
        return new PagedItems<AuditEntryEntity>(items, page, effectivePageSize, total);
    }
}

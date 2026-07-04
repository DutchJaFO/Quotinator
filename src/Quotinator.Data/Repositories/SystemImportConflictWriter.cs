using System.Data;
using Dapper.Contrib.Extensions;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;

namespace Quotinator.Data.Repositories;

/// <summary>
/// SQLite implementation of <see cref="ISystemImportConflictWriter"/>.
/// Extends <see cref="SqliteRepositoryBase{T}"/> directly — NOT <see cref="SqliteRepository{T}"/> —
/// so that the INSERT does not trigger an audit write for a table this project doesn't audit.
/// Dapper.Contrib generates the INSERT statement from the <c>[Table]</c> and <c>[Key]</c>
/// attributes on <see cref="SystemImportConflict"/>; no SQL string is required for writes.
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
}

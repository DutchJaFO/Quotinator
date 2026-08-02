using System.Data;
using Dapper.Contrib.Extensions;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;

namespace Quotinator.Data.Repositories;

/// <summary>
/// SQLite implementation of <see cref="IChangeWriter"/>.
/// Extends <see cref="SqliteRepositoryBase{T}"/> directly — NOT <see cref="SqliteRepository{T}"/> —
/// so that writing a change-log row never itself triggers an <c>Audit_Entry</c> write.
/// Dapper.Contrib generates the INSERT statement from the <c>[Table]</c> and <c>[ExplicitKey]</c>
/// (inherited from <see cref="Models.RecordBase"/>) attributes on <see cref="ChangeEntity"/>; no
/// SQL string is required for writes.
/// </summary>
public sealed class ChangeWriter : SqliteRepositoryBase<ChangeEntity>, IChangeWriter
{
    /// <summary>Initialises the writer with the connection factory.</summary>
    public ChangeWriter(IDbConnectionFactory factory) : base(factory) { }

    /// <inheritdoc/>
    public async Task LogAsync(ChangeEntity entry, IDbConnection connection, IDbTransaction? transaction = null)
        => await connection.InsertAsync(entry, transaction);

    /// <inheritdoc/>
    public async Task LogAsync(ChangeEntity entry)
    {
        using var conn = Factory.CreateConnection();
        conn.Open();
        await conn.InsertAsync(entry);
    }
}

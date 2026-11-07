using System.Data.Common;

namespace Quotinator.Data.Schema;

/// <summary>
/// Reads a database's schema into an engine-neutral <see cref="SchemaSnapshot"/> (#305). The one place the
/// integrity check depends on an engine: an implementation reads that engine's metadata, which ADR 003's
/// design goal 5 confines to <c>Quotinator.Data</c>'s implementations.
/// </summary>
public interface ISchemaSnapshotReader
{
    /// <summary>Reads every table, column, foreign key, index, view and trigger the connection's database holds.</summary>
    /// <param name="connection">An open connection to the database to read.</param>
    Task<SchemaSnapshot> ReadAsync(DbConnection connection);
}

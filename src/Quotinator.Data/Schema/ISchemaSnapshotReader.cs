using System.Data.Common;

namespace Quotinator.Data.Schema;

/// <summary>
/// Reads a database's schema into an engine-neutral <see cref="SchemaSnapshot"/> (#305). The one place the
/// integrity check depends on an engine: an implementation reads that engine's metadata and owns any
/// scratch database it needs, which ADR 003's design goal 5 confines to <c>Quotinator.Data</c>'s
/// implementations.
/// </summary>
public interface ISchemaSnapshotReader
{
    /// <summary>Reads every table, column, foreign key, index, view and trigger the connection's database holds.</summary>
    /// <param name="connection">An open connection to the database to read.</param>
    Task<SchemaSnapshot> ReadAsync(DbConnection connection);

    /// <summary>
    /// Runs <paramref name="scripts"/>, in order, against a scratch database that exists only for this call,
    /// and reads the schema they produce. Writes no file: this is how a database's expected schema is derived
    /// from its own migration path.
    /// </summary>
    /// <param name="scripts">The DDL a fresh database of the same kind would run.</param>
    Task<SchemaSnapshot> ReadScriptsAsync(IEnumerable<string> scripts);
}

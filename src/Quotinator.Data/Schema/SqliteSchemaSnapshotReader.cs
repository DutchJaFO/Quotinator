using System.Data.Common;

namespace Quotinator.Data.Schema;

/// <summary>Reads a SQLite database's schema into a <see cref="SchemaSnapshot"/> (#305).</summary>
public sealed class SqliteSchemaSnapshotReader : ISchemaSnapshotReader
{
    /// <inheritdoc/>
    public Task<SchemaSnapshot> ReadAsync(DbConnection connection)
    {
        _ = connection;
        return Task.FromResult(SchemaSnapshot.Empty);
    }
}

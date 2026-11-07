using System.Data.Common;
using Dapper;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Schema;

/// <summary>
/// Reads a SQLite database's schema into a <see cref="SchemaSnapshot"/> (#305). The only code in the
/// integrity check that knows the engine; its metadata reads live in <c>Sql.SchemaMetadata</c>.
/// </summary>
public sealed class SqliteSchemaSnapshotReader : ISchemaSnapshotReader
{
    private const string CreatedIndexOrigin = "c";
    private const string FilterKeyword = " WHERE ";

    /// <inheritdoc/>
    public async Task<SchemaSnapshot> ReadAsync(DbConnection connection)
    {
        List<SchemaTable> tables = [];
        foreach (string table in await connection.QueryAsync<string>(Sql.SchemaMetadata.SelectTables))
        {
            tables.Add(new SchemaTable(
                table,
                await ReadColumnsAsync(connection, table),
                await ReadForeignKeysAsync(connection, table),
                await ReadIndexesAsync(connection, table)));
        }

        return new SchemaSnapshot(
            tables,
            await ReadObjectsAsync(connection, "view"),
            await ReadObjectsAsync(connection, "trigger"));
    }

    private static async Task<IReadOnlyList<SchemaColumn>> ReadColumnsAsync(DbConnection connection, string table)
    {
        IEnumerable<(string Name, string? Type, long NotNull, string? Default, long PrimaryKey)> rows =
            await connection.QueryAsync<(string, string?, long, string?, long)>(Sql.SchemaMetadata.SelectColumns, new { table });

        return [.. rows.Select(r => new SchemaColumn(r.Name, r.Type ?? string.Empty, r.NotNull != 0, r.Default, (int)r.PrimaryKey))];
    }

    private static async Task<IReadOnlyList<SchemaForeignKey>> ReadForeignKeysAsync(DbConnection connection, string table)
    {
        IEnumerable<(long Seq, string Referenced, string From, string? To, string OnUpdate, string OnDelete)> rows =
            await connection.QueryAsync<(long, string, string, string?, string, string)>(Sql.SchemaMetadata.SelectForeignKeys, new { table });

        List<SchemaForeignKey> keys = [];
        List<string> columns = [];
        List<string> referenced = [];
        (string Table, string OnUpdate, string OnDelete)? current = null;

        foreach ((long seq, string referencedTable, string from, string? to, string onUpdate, string onDelete) in rows)
        {
            if (seq == 0 && current is not null)
            {
                keys.Add(new SchemaForeignKey([.. columns], current.Value.Table, [.. referenced], current.Value.OnDelete, current.Value.OnUpdate));
                columns.Clear();
                referenced.Clear();
            }

            current = (referencedTable, onUpdate, onDelete);
            columns.Add(from);
            referenced.Add(to ?? string.Empty);
        }

        if (current is not null)
            keys.Add(new SchemaForeignKey([.. columns], current.Value.Table, [.. referenced], current.Value.OnDelete, current.Value.OnUpdate));

        return keys;
    }

    private static async Task<IReadOnlyList<SchemaIndex>> ReadIndexesAsync(DbConnection connection, string table)
    {
        IEnumerable<(string Name, long Unique, string Origin, long Partial)> rows =
            await connection.QueryAsync<(string, long, string, long)>(Sql.SchemaMetadata.SelectIndexes, new { table });

        List<SchemaIndex> indexes = [];
        foreach ((string name, long unique, string origin, long partial) in rows)
        {
            IEnumerable<string?> columns = await connection.QueryAsync<string?>(Sql.SchemaMetadata.SelectIndexColumns, new { index = name });
            string? filter = partial != 0 ? await ReadFilterAsync(connection, name) : null;

            indexes.Add(new SchemaIndex(
                name,
                unique != 0,
                [.. columns.Select(c => c ?? "<expression>")],
                filter,
                origin != CreatedIndexOrigin));
        }

        return indexes;
    }

    private static async Task<string?> ReadFilterAsync(DbConnection connection, string index)
    {
        string? definition = await connection.ExecuteScalarAsync<string?>(Sql.SchemaMetadata.SelectIndexDefinition, new { index });
        if (definition is null)
            return null;

        int at = definition.IndexOf(FilterKeyword, StringComparison.OrdinalIgnoreCase);
        return at < 0 ? null : definition[(at + FilterKeyword.Length)..].Trim().TrimEnd(';').Trim();
    }

    private static async Task<IReadOnlyList<SchemaObject>> ReadObjectsAsync(DbConnection connection, string type)
    {
        IEnumerable<(string Name, string Table)> rows =
            await connection.QueryAsync<(string, string)>(Sql.SchemaMetadata.SelectObjectsOfType, new { type });

        return [.. rows.Select(r => new SchemaObject(r.Name, r.Table))];
    }
}

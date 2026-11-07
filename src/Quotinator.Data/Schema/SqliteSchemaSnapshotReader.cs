using System.Data.Common;
using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Schema;

/// <summary>
/// Reads a SQLite database's schema into a <see cref="SchemaSnapshot"/> (#305). The only code in the
/// integrity check that knows the engine; its metadata reads live in <c>Sql.SchemaMetadata</c>.
/// </summary>
public sealed class SqliteSchemaSnapshotReader : ISchemaSnapshotReader
{
    /// <summary>
    /// The one instance, exposed as the interface. The reader holds no state, and callers that cannot be
    /// handed one by the container, a static helper or a developer tool, hold the contract rather than the
    /// engine (ADR 003, design goal 5).
    /// </summary>
    public static ISchemaSnapshotReader Instance { get; } = new SqliteSchemaSnapshotReader();

    private const string CreatedIndexOrigin = "c";
    private const string FilterKeyword = " WHERE ";

    // An in-memory database that disappears when its one connection closes, so the scratch schema
    // writes no file and works on a read-only data directory.
    private const string ScratchConnectionString = "Data Source=:memory:";

    /// <inheritdoc/>
    public async Task<SchemaSnapshot> ReadScriptsAsync(IEnumerable<string> scripts)
    {
        // A scratch database that exists only for this read: nothing in the container could supply it,
        // and it must never be an application connection, so it is created here.
        using SqliteConnection scratch = new(ScratchConnectionString);
        await scratch.OpenAsync();

        // Migrations that rebuild a table drop it while others still reference it; the application
        // runs them with enforcement off, so the scratch replay does too.
        await scratch.ExecuteAsync("PRAGMA foreign_keys = OFF;");

        foreach (string script in scripts)
            await scratch.ExecuteAsync(script);

        return await ReadAsync(scratch);
    }

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

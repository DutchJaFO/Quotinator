using Microsoft.Data.Sqlite;
using Quotinator.Data.Enums;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Database.Checks;

/// <summary>
/// References (#490, ADR 028): whether every declared reference points at a row that exists, through
/// <c>PRAGMA foreign_key_check</c>, which the documentation states without any condition on the connection's
/// <c>foreign_keys</c> setting, so the check never sets it (https://www.sqlite.org/pragma.html#pragma_foreign_key_check).
/// Full depth only.
/// </summary>
internal sealed class ReferencesLayer : QueryLayer
{
    /// <inheritdoc/>
    public override DatabaseCheckLayer Layer => DatabaseCheckLayer.References;

    /// <inheritdoc/>
    public override bool RunsAtFast => false;

    /// <inheritdoc/>
    protected override async Task<DatabaseCheckLayerResult> CheckAsync(SqliteConnection connection, SqliteCheckSubject subject)
    {
        // One row per violation: the referencing table, its rowid (null for a WITHOUT ROWID table), the table
        // referenced, and the index of the foreign key in the referencing table's list.
        List<string> violations = [];
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = Sql.SchemaMetadata.ForeignKeyCheck;
        using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string rowId = reader.IsDBNull(1) ? "a row" : $"row {reader.GetInt64(1)}";
            violations.Add($"{reader.GetString(0)} {rowId} references a missing row in {reader.GetString(2)}");
        }

        return violations.Count == 0 ? LayerResults.Passed(Layer) : LayerResults.Failed(Layer, LayerResults.Join(violations));
    }
}

using Microsoft.Data.Sqlite;
using Quotinator.Data.Enums;
using Quotinator.Data.Schema;

namespace Quotinator.Data.Database.Checks;

/// <summary>
/// Schema (#490, ADR 028): whether the schema matches the one its migration path defines, by #305's comparison.
/// It reads the database through the check's own connection, so it opens nothing for writing. Extra objects
/// leave a schema intact, as in #305.
/// </summary>
internal sealed class SchemaLayer : QueryLayer
{
    /// <inheritdoc/>
    public override DatabaseCheckLayer Layer => DatabaseCheckLayer.Schema;

    /// <inheritdoc/>
    public override bool RunsAtFast => true;

    /// <inheritdoc/>
    protected override async Task<DatabaseCheckLayerResult> CheckAsync(SqliteConnection connection, SqliteCheckSubject subject)
    {
        SchemaSnapshot expected = await SqliteSchemaSnapshotReader.Instance.ReadScriptsAsync(subject.ExpectedSchemaScripts);
        SchemaSnapshot actual = await SqliteSchemaSnapshotReader.Instance.ReadAsync(connection);
        SchemaIntegrityResult result = SchemaIntegrityComparer.Compare(expected, actual);

        if (result.IsIntact)
            return LayerResults.Passed(Layer);

        return LayerResults.Failed(Layer, LayerResults.Join(
        [
            .. result.Differences
                .Where(d => d.Kind != SchemaDifferenceKind.Extra)
                .Select(d => d.Aspect is null
                    ? $"{d.Kind} {d.ObjectKind} {d.ObjectName}"
                    : $"{d.Kind} {d.ObjectKind} {d.ObjectName} ({d.Aspect}: expected '{d.Expected}', actual '{d.Actual}')"),
        ]));
    }
}

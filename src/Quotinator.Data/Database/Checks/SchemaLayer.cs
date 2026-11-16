using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Enums;
using Quotinator.Data.Queries;
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
        // #490, found in T1: before its migrations run, an old database's schema differs from this build's by
        // design, and the migrations will fix it, so a comparison then says nothing. That is the only skip
        // (developer, 2026-11-16): a database ahead of this build has nothing that will fix it, and comparing it is
        // what shows what the overshoot consists of.
        if (await WhyNotYetComparableAsync(connection, subject) is string whyNot)
            return LayerResults.Skipped(Layer, whyNot);

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

    /// <summary>
    /// Why the schema cannot be compared yet: the first version table that is missing or behind this build, so
    /// migrations are still to run and will change the schema, or <see langword="null"/> when none is. A table
    /// ahead of this build does not stop the comparison.
    /// </summary>
    private static async Task<string?> WhyNotYetComparableAsync(SqliteConnection connection, SqliteCheckSubject subject)
    {
        foreach ((SchemaVersionTable table, int expected) in subject.ExpectedSchemaVersions)
        {
            string name = Sql.SchemaMetadata.VersionTableName(table);
            if (await connection.ExecuteScalarAsync<int>(Sql.SchemaMetadata.TableExists, new { name }) == 0)
                return $"{name} does not exist, so the database records no migration: its schema is compared once its migrations have run.";

            int recorded = await connection.ExecuteScalarAsync<int>(Sql.SchemaMetadata.SelectRecordedVersion(table));
            if (recorded < expected)
                return $"{name} records version {recorded}, and this build's is {expected}: the schema is compared once the migrations that bring it to this build's have run.";
        }

        return null;
    }
}

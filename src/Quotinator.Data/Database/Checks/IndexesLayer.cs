using Microsoft.Data.Sqlite;
using Quotinator.Data.Enums;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Database.Checks;

/// <summary>
/// Indexes (#490, ADR 028): whether indexes agree with their tables and unique constraints hold, through
/// <c>PRAGMA integrity_check</c>, which adds those to what the Pages layer already checked
/// (https://www.sqlite.org/pragma.html#pragma_integrity_check). Full depth only.
/// </summary>
internal sealed class IndexesLayer : QueryLayer
{
    /// <inheritdoc/>
    public override DatabaseCheckLayer Layer => DatabaseCheckLayer.Indexes;

    /// <inheritdoc/>
    public override bool RunsAtFast => false;

    /// <inheritdoc/>
    protected override Task<DatabaseCheckLayerResult> CheckAsync(SqliteConnection connection, SqliteCheckSubject subject) =>
        OkPragmaAsync(connection, Sql.SchemaMetadata.IntegrityCheck);
}

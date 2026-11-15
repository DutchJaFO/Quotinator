using Microsoft.Data.Sqlite;
using Quotinator.Data.Enums;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Database.Checks;

/// <summary>
/// Pages (#490, ADR 028): whether every page can be read and every record is well-formed, through
/// <c>PRAGMA quick_check</c>, which does not compare index content with table content
/// (https://www.sqlite.org/pragma.html#pragma_quick_check).
/// </summary>
internal sealed class PagesLayer : QueryLayer
{
    /// <inheritdoc/>
    public override DatabaseCheckLayer Layer => DatabaseCheckLayer.Pages;

    /// <inheritdoc/>
    public override bool RunsAtFast => true;

    /// <inheritdoc/>
    protected override Task<DatabaseCheckLayerResult> CheckAsync(SqliteConnection connection, SqliteCheckSubject subject) =>
        OkPragmaAsync(connection, Sql.SchemaMetadata.QuickCheck);
}

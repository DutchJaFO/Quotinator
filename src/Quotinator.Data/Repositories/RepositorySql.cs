using System.Text.RegularExpressions;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Repositories;

/// <summary>SQL factory methods for <see cref="SqliteRepository{T}"/> and <see cref="SqliteRestorableRepository{T}"/>.</summary>
/// <remarks>
/// Table names come from the <c>[Table]</c> attribute on the entity type — developer-controlled
/// metadata, not user input. Interpolating them into SQL is safe; SQLite does not support
/// parameterised identifiers, so string interpolation is the only viable mechanism.
/// </remarks>
internal static class RepositorySql
{
    private static readonly Regex IdentifierPattern = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private static readonly IReadOnlyList<SortColumn> DefaultOrderBy = [new SortColumn("DateCreated")];

    /// <summary>The primary key column name every generic query in this class compares against.</summary>
    private const string IdColumn = "Id";

    /// <summary>The bound parameter name every generic query in this class matches <see cref="IdColumn"/> against.</summary>
    private const string IdParam = "id";

    /// <summary>
    /// Builds an explicit SELECT column list from <paramref name="columns"/>, wrapping every column in
    /// <paramref name="columns"/>.<see cref="IEntityColumnMetadata.IdColumnNames"/> via
    /// <see cref="IdClauses.SelectColumn(string,string?)"/> — never a bare <c>SELECT *</c>, so a
    /// string-typed id column (should one ever exist on a domain entity) gets the same read-time
    /// presentation normalization every hand-written query in <c>Sql.cs</c> already has. See ADR 012.
    /// Also shared by <c>Sql.ImportBatches</c> (#212) — a hand-written, domain-specific query set that
    /// needs the same reflection-driven column list this class already builds, without duplicating the
    /// logic. <c>internal</c> rather than <c>private</c> for that reuse; behaviour is unchanged.
    /// </summary>
    internal static string BuildSelectColumns(IEntityColumnMetadata columns)
        => string.Join(", ", columns.ValidColumnNames.Select(c =>
            columns.IdColumnNames.Contains(c) ? IdClauses.SelectColumn(c) : c));

    /// <summary>Selects an active record by primary key. Case-insensitive (#210) via <see cref="IdClauses"/> — every id-comparison query in this codebase is, per ADR 012, regardless of what casing the entity reached through this generic layer happens to already bind its Guid parameter as today.</summary>
    internal static string SelectById(string tableName, IEntityColumnMetadata columns)
        => $"SELECT {BuildSelectColumns(columns)} FROM {tableName} WHERE {IdClauses.Equals(IdColumn, IdParam)} AND IsDeleted = 0";

    /// <summary>Soft-deletes a record by primary key. Case-insensitive — see <see cref="SelectById"/>'s remark.</summary>
    internal static string SoftDelete(string tableName)
        => $"UPDATE {tableName} SET IsDeleted = 1, DateDeleted = @now, DateModified = @now WHERE {IdClauses.Equals(IdColumn, IdParam)} AND IsDeleted = 0;";

    /// <summary>Selects all soft-deleted records in the table.</summary>
    internal static string SelectDeleted(string tableName, IEntityColumnMetadata columns)
        => $"SELECT {BuildSelectColumns(columns)} FROM {tableName} WHERE IsDeleted = 1";

    /// <summary>Restores a soft-deleted record by primary key. Case-insensitive — see <see cref="SelectById"/>'s remark.</summary>
    internal static string Restore(string tableName)
        => $"UPDATE {tableName} SET IsDeleted = 0, DateDeleted = NULL, DateModified = @now WHERE {IdClauses.Equals(IdColumn, IdParam)} AND IsDeleted = 1";

    /// <summary>Hard-deletes a soft-deleted record by primary key. Case-insensitive — see <see cref="SelectById"/>'s remark.</summary>
    internal static string HardDelete(string tableName)
        => $"DELETE FROM {tableName} WHERE {IdClauses.Equals(IdColumn, IdParam)} AND IsDeleted = 1";

    /// <summary>Purges all soft-deleted records from the table.</summary>
    internal static string Purge(string tableName)
        => $"DELETE FROM {tableName} WHERE IsDeleted = 1";

    /// <summary>
    /// Selects the active detail record whose <paramref name="fkColumn"/> matches the given parent ID.
    /// Used by <see cref="SqliteOneToOneRepository{TParent,TDetail}"/> for separate-FK layouts.
    /// Case-insensitive — see <see cref="SelectById"/>'s remark.
    /// </summary>
    internal static string SelectByForeignKey(string tableName, string fkColumn, IEntityColumnMetadata columns)
        => $"SELECT {BuildSelectColumns(columns)} FROM [{tableName}] WHERE {IdClauses.Equals($"[{fkColumn}]", "parentId")} AND [IsDeleted] = 0";

    /// <summary>
    /// Selects a junction row by the two FK columns — active or soft-deleted.
    /// No <c>IsDeleted</c> filter: <see cref="SqliteLinkRepository{TLeft,TRight,TJunction}"/>
    /// needs to see soft-deleted rows to decide whether to restore or insert.
    /// Case-insensitive — see <see cref="SelectById"/>'s remark.
    /// </summary>
    internal static string SelectJunctionRow(string tableName, string leftFkColumn, string rightFkColumn, IEntityColumnMetadata columns)
        => $"SELECT {BuildSelectColumns(columns)} FROM [{tableName}] WHERE {IdClauses.Equals($"[{leftFkColumn}]", "leftId")} AND {IdClauses.Equals($"[{rightFkColumn}]", "rightId")}";

    /// <summary>
    /// Selects a set of active records by primary key list.
    /// Dapper expands <c>@ids</c> from any <see cref="System.Collections.Generic.IEnumerable{T}"/> automatically.
    /// The column side is LOWER()-wrapped (#210); protecting the list-parameter side is the caller's
    /// responsibility (canonicalize every id in the list before binding), the same as
    /// <c>Sql.CharacterSources.SelectSourceReferencesForCharacters</c>'s equivalent IN clause.
    /// </summary>
    internal static string SelectByIds(string tableName, IEntityColumnMetadata columns)
        => $"SELECT {BuildSelectColumns(columns)} FROM [{tableName}] WHERE {IdClauses.In("[Id]", "ids")} AND [IsDeleted] = 0";

    /// <summary>
    /// Selects a page of active records, ordered by <paramref name="orderBy"/> (defaulting to
    /// <c>DateCreated</c> ascending when <see langword="null"/> or empty) with <c>Id</c> always
    /// appended last as a tiebreaker, so no row is ever repeated or skipped across pages.
    /// </summary>
    /// <remarks>
    /// Each <see cref="SortColumn.Name"/> must be a bare identifier. Unlike <paramref name="tableName"/>
    /// — auto-derived from <c>[Table]</c> via reflection, structurally impossible for user input to
    /// reach — <see cref="SortColumn.Name"/> is an explicit argument at each call site, so it gets its
    /// own guard here. Whether the name is an actual column on the entity is validated separately, by
    /// the caller (<see cref="SqliteRepository{T}.GetPageAsync"/>), which knows <c>T</c> and can check
    /// against <paramref name="columns"/>.<see cref="IEntityColumnMetadata.ValidColumnNames"/> — this
    /// method only knows <paramref name="tableName"/> as a string, so its own <see cref="IdentifierPattern"/>
    /// check (a syntax check, not a membership check) is a separate, narrower defence, not a substitute.
    /// </remarks>
    internal static string SelectPage(string tableName, IEntityColumnMetadata columns, IReadOnlyList<SortColumn>? orderBy = null)
    {
        var sortColumns = orderBy is { Count: > 0 } ? orderBy : DefaultOrderBy;
        foreach (var col in sortColumns)
            if (!IdentifierPattern.IsMatch(col.Name))
                throw new ArgumentException($"'{col.Name}' is not a valid column identifier.", nameof(orderBy));

        var clause = string.Join(", ", sortColumns.Select(c => c.Descending ? $"{c.Name} DESC" : c.Name));
        return $"SELECT {BuildSelectColumns(columns)} FROM {tableName} WHERE IsDeleted = 0 ORDER BY {clause}, Id LIMIT @limit OFFSET @offset";
    }

    /// <summary>Counts active (non-deleted) records in the table.</summary>
    internal static string CountActive(string tableName)
        => $"SELECT COUNT(*) FROM {tableName} WHERE IsDeleted = 0";
}

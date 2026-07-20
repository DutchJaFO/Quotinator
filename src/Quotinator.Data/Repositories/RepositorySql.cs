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

    /// <summary>Selects an active record by primary key. Case-insensitive (#210) via <see cref="IdClauses"/> — every id-comparison query in this codebase is, per ADR 012, regardless of whether the entity reached through this generic layer happens to already bind its Guid parameter uppercased today.</summary>
    internal static string SelectById(string tableName)
        => $"SELECT * FROM {tableName} WHERE {IdClauses.Equals("Id", "id")} AND IsDeleted = 0";

    /// <summary>Soft-deletes a record by primary key. Case-insensitive — see <see cref="SelectById"/>'s remark.</summary>
    internal static string SoftDelete(string tableName)
        => $"UPDATE {tableName} SET IsDeleted = 1, DateDeleted = @now, DateModified = @now WHERE {IdClauses.Equals("Id", "id")} AND IsDeleted = 0;";

    /// <summary>Selects all soft-deleted records in the table.</summary>
    internal static string SelectDeleted(string tableName)
        => $"SELECT * FROM {tableName} WHERE IsDeleted = 1";

    /// <summary>Restores a soft-deleted record by primary key. Case-insensitive — see <see cref="SelectById"/>'s remark.</summary>
    internal static string Restore(string tableName)
        => $"UPDATE {tableName} SET IsDeleted = 0, DateDeleted = NULL, DateModified = @now WHERE {IdClauses.Equals("Id", "id")} AND IsDeleted = 1";

    /// <summary>Hard-deletes a soft-deleted record by primary key. Case-insensitive — see <see cref="SelectById"/>'s remark.</summary>
    internal static string HardDelete(string tableName)
        => $"DELETE FROM {tableName} WHERE {IdClauses.Equals("Id", "id")} AND IsDeleted = 1";

    /// <summary>Purges all soft-deleted records from the table.</summary>
    internal static string Purge(string tableName)
        => $"DELETE FROM {tableName} WHERE IsDeleted = 1";

    /// <summary>
    /// Selects the active detail record whose <paramref name="fkColumn"/> matches the given parent ID.
    /// Used by <see cref="SqliteOneToOneRepository{TParent,TDetail}"/> for separate-FK layouts.
    /// Case-insensitive — see <see cref="SelectById"/>'s remark.
    /// </summary>
    internal static string SelectByForeignKey(string tableName, string fkColumn)
        => $"SELECT * FROM [{tableName}] WHERE {IdClauses.Equals($"[{fkColumn}]", "parentId")} AND [IsDeleted] = 0";

    /// <summary>
    /// Selects a junction row by the two FK columns — active or soft-deleted.
    /// No <c>IsDeleted</c> filter: <see cref="SqliteLinkRepository{TLeft,TRight,TJunction}"/>
    /// needs to see soft-deleted rows to decide whether to restore or insert.
    /// Case-insensitive — see <see cref="SelectById"/>'s remark.
    /// </summary>
    internal static string SelectJunctionRow(string tableName, string leftFkColumn, string rightFkColumn)
        => $"SELECT * FROM [{tableName}] WHERE {IdClauses.Equals($"[{leftFkColumn}]", "leftId")} AND {IdClauses.Equals($"[{rightFkColumn}]", "rightId")}";

    /// <summary>
    /// Selects a set of active records by primary key list.
    /// Dapper expands <c>@ids</c> from any <see cref="System.Collections.Generic.IEnumerable{T}"/> automatically.
    /// The column side is UPPER()-wrapped (#210); protecting the list-parameter side is the caller's
    /// responsibility (canonicalize every id in the list before binding), the same as
    /// <c>Sql.CharacterSources.SelectSourceReferencesForCharacters</c>'s equivalent IN clause.
    /// </summary>
    internal static string SelectByIds(string tableName)
        => $"SELECT * FROM [{tableName}] WHERE {IdClauses.In("[Id]", "ids")} AND [IsDeleted] = 0";

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
    /// against <see cref="SqliteRepositoryBase{T}.ValidColumnNames"/> — this method only knows
    /// <paramref name="tableName"/> as a string and cannot make that check itself.
    /// </remarks>
    internal static string SelectPage(string tableName, IReadOnlyList<SortColumn>? orderBy = null)
    {
        var columns = orderBy is { Count: > 0 } ? orderBy : DefaultOrderBy;
        foreach (var col in columns)
            if (!IdentifierPattern.IsMatch(col.Name))
                throw new ArgumentException($"'{col.Name}' is not a valid column identifier.", nameof(orderBy));

        var clause = string.Join(", ", columns.Select(c => c.Descending ? $"{c.Name} DESC" : c.Name));
        return $"SELECT * FROM {tableName} WHERE IsDeleted = 0 ORDER BY {clause}, Id LIMIT @limit OFFSET @offset";
    }

    /// <summary>Counts active (non-deleted) records in the table.</summary>
    internal static string CountActive(string tableName)
        => $"SELECT COUNT(*) FROM {tableName} WHERE IsDeleted = 0";
}

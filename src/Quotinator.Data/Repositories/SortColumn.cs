namespace Quotinator.Data.Repositories;

/// <summary>A single column in an <c>ORDER BY</c> clause and its sort direction.</summary>
/// <param name="Name">The column name. Must be a bare identifier — see <see cref="RepositorySql.SelectPage"/>.</param>
/// <param name="Descending">When <see langword="true"/>, sorts this column descending. Ascending by default.</param>
public readonly record struct SortColumn(string Name, bool Descending = false);

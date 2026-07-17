using System.Reflection;
using Dapper.Contrib.Extensions;
using Quotinator.Data.Connections;

namespace Quotinator.Data.Repositories;

/// <summary>
/// Infrastructure base for all SQLite repositories in Quotinator.Data.
/// Provides the connection factory and the table name resolved from the <c>[Table]</c> attribute.
/// Does not write audit entries — derive from <see cref="SqliteRepository{T}"/> for auditing,
/// or from this class directly when audit recursion must be avoided (e.g. <see cref="SystemAuditWriter"/>).
/// </summary>
/// <typeparam name="T">Entity type. Must carry a <c>[Table]</c> attribute from Dapper.Contrib.Extensions.</typeparam>
public abstract class SqliteRepositoryBase<T> where T : class
{
    /// <summary>Factory used to open connections. Accessible to derived repository classes.</summary>
    protected readonly IDbConnectionFactory Factory;

    // Resolved once per T. The table name comes from the [Table] attribute — developer-controlled
    // metadata, not user input. See RepositorySql for why interpolating it into SQL is safe.
    /// <summary>SQLite table name resolved from the <c>[Table]</c> attribute on <typeparamref name="T"/>.</summary>
    protected static readonly string TableName =
        typeof(T).GetCustomAttribute<TableAttribute>()?.Name
        ?? throw new InvalidOperationException(
            $"{typeof(T).Name} must carry a [Table(\"..\")] attribute from Dapper.Contrib.Extensions.");

    // Property names double as column names for every entity today — no [Column] remapping exists
    // anywhere in the codebase. [Write(false)]/[Computed] properties are excluded because Dapper.Contrib
    // never persists them, so they are not real columns a query could sort by.
    /// <summary>Column names <typeparamref name="T"/> actually persists — used to validate a caller-supplied sort column before it reaches SQL.</summary>
    protected static readonly HashSet<string> ValidColumnNames = new(
        typeof(T).GetProperties()
            .Where(p => p.GetCustomAttribute<WriteAttribute>()?.Write != false
                     && p.GetCustomAttribute<ComputedAttribute>() is null)
            .Select(p => p.Name),
        StringComparer.Ordinal);

    /// <summary>Initialises the base with the connection factory.</summary>
    protected SqliteRepositoryBase(IDbConnectionFactory factory)
    {
        Factory = factory;
    }

    /// <summary>
    /// Inserts a collection of entities. Data only — no audit entries are written.
    /// <see cref="SqliteRepository{T}"/> overrides this to add audit trail support.
    /// </summary>
    /// <param name="entities">Entities to insert.</param>
    /// <param name="unitOfWork">Optional. When supplied, the inserts participate in the caller's transaction.</param>
    /// <param name="strategy">
    /// Controls whether all entities are inserted in a single SQL call (<see cref="InsertStrategy.Bulk"/>)
    /// or individually per entity (<see cref="InsertStrategy.Sequential"/>).
    /// </param>
    public virtual async Task InsertManyAsync(
        IEnumerable<T> entities,
        IUnitOfWork? unitOfWork = null,
        InsertStrategy strategy = InsertStrategy.Bulk)
    {
        var list = entities.ToList();
        await TransactionScope.ExecuteAsync(Factory, async uow =>
        {
            var sqlite = (SqliteUnitOfWork)uow;
            if (strategy == InsertStrategy.Bulk)
                await sqlite.Connection.InsertAsync(list, sqlite.Transaction);
            else
                foreach (var entity in list)
                    await sqlite.Connection.InsertAsync(entity, sqlite.Transaction);
        }, unitOfWork);
    }
}

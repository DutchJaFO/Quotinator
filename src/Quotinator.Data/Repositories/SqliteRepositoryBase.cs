using System.Reflection;
using Dapper.Contrib.Extensions;
using Quotinator.Data.Connections;

namespace Quotinator.Data.Repositories;

/// <summary>
/// Infrastructure base for all SQLite repositories in Quotinator.Data.
/// Provides the connection factory and the table name resolved from the <c>[Table]</c> attribute.
/// Does not write audit entries — derive from <see cref="SqliteRepository{T}"/> for auditing,
/// or from this class directly when audit recursion must be avoided (e.g. <see cref="AuditWriter"/>).
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

    /// <summary>Initialises the base with the connection factory.</summary>
    protected SqliteRepositoryBase(IDbConnectionFactory factory)
    {
        Factory = factory;
    }
}

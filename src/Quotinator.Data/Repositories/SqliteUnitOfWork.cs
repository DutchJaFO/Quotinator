using System.Data;
using Quotinator.Data.Data;

namespace Quotinator.Data.Repositories;

/// <summary>
/// SQLite implementation of <see cref="IUnitOfWork"/>.
/// Owns one <see cref="IDbConnection"/> and one <see cref="IDbTransaction"/> for the duration of a unit of work.
/// Disposing without calling <see cref="CommitAsync"/> rolls back the transaction automatically.
/// </summary>
public sealed class SqliteUnitOfWork : IUnitOfWork
{
    private readonly IDbConnectionFactory _factory;
    private bool _finalised;

    /// <summary>The open connection for this unit of work. Accessible to repository classes in this assembly.</summary>
    internal IDbConnection Connection { get; private set; } = null!;

    /// <summary>The active transaction for this unit of work. Accessible to repository classes in this assembly.</summary>
    internal IDbTransaction? Transaction { get; private set; }

    /// <summary>Initialises the unit of work with the factory used to open SQLite connections.</summary>
    /// <param name="factory">Opens connections to the SQLite database.</param>
    public SqliteUnitOfWork(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <inheritdoc/>
    public Task BeginTransactionAsync()
    {
        Connection = _factory.CreateConnection();
        Connection.Open();
        Transaction = Connection.BeginTransaction();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task CommitAsync()
    {
        Transaction?.Commit();
        _finalised = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RollbackAsync()
    {
        Transaction?.Rollback();
        _finalised = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (!_finalised)
            Transaction?.Rollback();
        Transaction?.Dispose();
        Connection?.Dispose();
        return ValueTask.CompletedTask;
    }
}

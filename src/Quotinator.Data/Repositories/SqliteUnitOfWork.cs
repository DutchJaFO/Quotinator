using System.Data;
using Quotinator.Data.Connections;

namespace Quotinator.Data.Repositories;

/// <summary>
/// SQLite implementation of <see cref="IUnitOfWork"/>.
/// Owns one <see cref="IDbConnection"/> and one <see cref="IDbTransaction"/> for the duration of a unit of work.
/// Disposing without calling <see cref="CommitAsync"/> rolls back the transaction automatically.
/// </summary>
public sealed class SqliteUnitOfWork : IUnitOfWork
{
    private readonly IDbConnectionFactory? _factory;
    private readonly bool _ownsConnection;
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
        _ownsConnection = true;
    }

    /// <summary>
    /// Wraps a connection and transaction some other owner (e.g. <c>IImportActionCoordinator</c>'s
    /// batch-scoped callback) already opened and will commit/roll back/dispose itself. This instance
    /// never opens, commits, rolls back, or disposes the wrapped connection/transaction — it exists
    /// only so repository calls that require an <see cref="IUnitOfWork"/> can participate in a
    /// transaction they do not own.
    /// </summary>
    /// <param name="connection">An already-open connection owned by the caller.</param>
    /// <param name="transaction">An already-active transaction owned by the caller.</param>
    internal SqliteUnitOfWork(IDbConnection connection, IDbTransaction transaction)
    {
        Connection = connection;
        Transaction = transaction;
        _ownsConnection = false;
        _finalised = true;
    }

    /// <inheritdoc/>
    public Task BeginTransactionAsync()
    {
        if (!_ownsConnection)
            return Task.CompletedTask;

        Connection = _factory!.CreateConnection();
        Connection.Open();
        Transaction = Connection.BeginTransaction();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task CommitAsync()
    {
        if (!_ownsConnection)
            return Task.CompletedTask;

        Transaction?.Commit();
        _finalised = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RollbackAsync()
    {
        if (!_ownsConnection)
            return Task.CompletedTask;

        Transaction?.Rollback();
        _finalised = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (!_ownsConnection)
            return ValueTask.CompletedTask;

        if (!_finalised)
            Transaction?.Rollback();
        Transaction?.Dispose();
        Connection?.Dispose();
        return ValueTask.CompletedTask;
    }
}

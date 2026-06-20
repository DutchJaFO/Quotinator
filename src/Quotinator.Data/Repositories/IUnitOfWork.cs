namespace Quotinator.Data.Repositories;

/// <summary>
/// Owns a single database connection and transaction for use across multiple repository operations.
/// Create via dependency injection; pass to repository methods that should participate in the transaction.
/// Disposing without calling <see cref="CommitAsync"/> rolls back all operations automatically.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>Opens the connection and begins a database transaction.</summary>
    Task BeginTransactionAsync();

    /// <summary>Commits all operations within this unit of work to the database.</summary>
    Task CommitAsync();

    /// <summary>Discards all operations within this unit of work.</summary>
    Task RollbackAsync();
}

using Quotinator.Data.Connections;

namespace Quotinator.Data.Repositories;

/// <summary>
/// Reusable transaction coordination helper. Joins an existing <see cref="IUnitOfWork"/>
/// or creates and commits its own when none is provided.
/// </summary>
public static class TransactionScope
{
    /// <summary>
    /// Executes <paramref name="work"/> inside a unit of work.
    /// When <paramref name="existing"/> is supplied the work joins it; the caller remains
    /// responsible for committing or rolling back.
    /// When <paramref name="existing"/> is <c>null</c> a new <see cref="SqliteUnitOfWork"/> is created,
    /// committed on success, and rolled back automatically on exception.
    /// </summary>
    public static async Task ExecuteAsync(
        IDbConnectionFactory factory,
        Func<IUnitOfWork, Task> work,
        IUnitOfWork? existing = null)
    {
        if (existing != null)
        {
            await work(existing);
            return;
        }
        await using var uow = new SqliteUnitOfWork(factory);
        await uow.BeginTransactionAsync();
        await work(uow);
        await uow.CommitAsync();
    }
}

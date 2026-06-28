using Quotinator.Data.Connections;
using Quotinator.Data.Models;

namespace Quotinator.Data.Repositories;

/// <summary>
/// Generic base for repositories that own a parent entity and its associated child collection.
/// Inserts the parent and all children atomically within a single transaction.
/// Navigation properties populated by the caller via <see cref="GetChildren"/> are write-only;
/// reads go through <c>JoinQueryRepository</c> / <c>IJoinStrategy</c>.
/// </summary>
/// <typeparam name="TParent">Parent entity type.</typeparam>
/// <typeparam name="TChild">Child entity type.</typeparam>
public abstract class AggregateRepository<TParent, TChild>(
    IDbConnectionFactory factory,
    IAuditWriter auditWriter,
    ICallerContext callerContext)
    : SqliteRepository<TParent>(factory, auditWriter, callerContext)
    where TParent : RecordBase
    where TChild  : RecordBase
{
    /// <summary>Returns the child entities that belong to <paramref name="parent"/> and must be inserted alongside it.</summary>
    protected abstract IReadOnlyList<TChild> GetChildren(TParent parent);

    /// <summary>The repository used to insert child entities. Must be provided by the concrete subclass.</summary>
    protected abstract SqliteRepository<TChild> ChildRepository { get; }

    /// <summary>
    /// Insert strategy applied to the child collection. Defaults to <see cref="InsertStrategy.Bulk"/>.
    /// Override to <see cref="InsertStrategy.Sequential"/> when per-row error identification is required.
    /// </summary>
    protected virtual InsertStrategy ChildInsertStrategy => InsertStrategy.Bulk;

    /// <inheritdoc/>
    public override async Task InsertAsync(TParent parent, IUnitOfWork? unitOfWork = null)
    {
        await TransactionScope.ExecuteAsync(Factory, async uow =>
        {
            await base.InsertAsync(parent, uow);
            await ChildRepository.InsertManyAsync(GetChildren(parent), uow, ChildInsertStrategy);
        }, unitOfWork);
    }
}

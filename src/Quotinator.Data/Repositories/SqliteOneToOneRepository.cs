using System.Reflection;
using Dapper;
using Dapper.Contrib.Extensions;
using Quotinator.Data.Connections;
using Quotinator.Data.Models;

namespace Quotinator.Data.Repositories;

/// <summary>
/// Abstract base for repositories that pair exactly one parent entity with exactly one detail entity.
/// Extends <see cref="AggregateRepository{TParent,TDetail}"/> (which handles atomic writes via
/// <see cref="TransactionScope"/>) and adds the read side via <see cref="GetDetailAsync"/>.
/// </summary>
/// <remarks>
/// Two protected helpers cover the two standard layouts:
/// <list type="bullet">
/// <item><see cref="GetDetailBySharedKeyAsync"/> — shared-PK layout: detail.Id equals parent.Id.</item>
/// <item><see cref="GetDetailByForeignKeyAsync"/> — separate-FK layout: detail has its own PK and a FK column.</item>
/// </list>
/// Concrete subclasses implement <see cref="GetDetailAsync"/> by delegating to the appropriate helper.
/// </remarks>
/// <typeparam name="TParent">The primary entity type.</typeparam>
/// <typeparam name="TDetail">The detail entity type.</typeparam>
public abstract class SqliteOneToOneRepository<TParent, TDetail>(
    IDbConnectionFactory factory,
    ISystemAuditWriter auditWriter,
    ICallerContext callerContext)
    : AggregateRepository<TParent, TDetail>(factory, auditWriter, callerContext),
      IOneToOneRepository<TParent, TDetail>
    where TParent : RecordBase
    where TDetail : RecordBase
{
    // Resolved once per TDetail — same pattern as SqliteRepositoryBase.TableName for TParent.
    private static readonly string DetailTableName =
        typeof(TDetail).GetCustomAttribute<TableAttribute>()?.Name
        ?? throw new InvalidOperationException(
            $"{typeof(TDetail).Name} must carry a [Table(\"..\")] attribute from Dapper.Contrib.Extensions.");

    /// <inheritdoc/>
    public abstract Task<TDetail?> GetDetailAsync(Guid parentId, IUnitOfWork? unitOfWork = null);

    /// <summary>
    /// Helper for shared-primary-key layouts.
    /// Loads the detail record whose <c>Id</c> equals <paramref name="parentId"/>.
    /// </summary>
    protected Task<TDetail?> GetDetailBySharedKeyAsync(Guid parentId, IUnitOfWork? unitOfWork = null)
        => ChildRepository.GetByIdAsync(parentId, unitOfWork);

    /// <summary>
    /// Helper for separate-foreign-key layouts.
    /// Loads the active detail record whose <paramref name="fkColumn"/> matches <paramref name="parentId"/>.
    /// </summary>
    protected async Task<TDetail?> GetDetailByForeignKeyAsync(
        string fkColumn, Guid parentId, IUnitOfWork? unitOfWork = null)
    {
        var param = new { parentId = parentId.ToString("D").ToUpperInvariant() };
        if (unitOfWork is SqliteUnitOfWork uow)
        {
            var rows = await uow.Connection.QueryAsync<TDetail>(
                RepositorySql.SelectByForeignKey(DetailTableName, fkColumn), param, uow.Transaction);
            return rows.FirstOrDefault();
        }
        using var conn = Factory.CreateConnection();
        conn.Open();
        var result = await conn.QueryAsync<TDetail>(
            RepositorySql.SelectByForeignKey(DetailTableName, fkColumn), param);
        return result.FirstOrDefault();
    }
}

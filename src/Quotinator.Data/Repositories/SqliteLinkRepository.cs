using System.Reflection;
using Dapper;
using Dapper.Contrib.Extensions;
using Quotinator.Data.Connections;
using Quotinator.Data.Models;

namespace Quotinator.Data.Repositories;

/// <summary>
/// Abstract base class for many-to-many link repositories.
/// Manages a junction table between <typeparamref name="TLeft"/> and <typeparamref name="TRight"/>
/// using a soft-deletable <typeparamref name="TJunction"/> entity.
/// </summary>
/// <typeparam name="TLeft">Left-side entity. Must carry a <c>[Table]</c> attribute.</typeparam>
/// <typeparam name="TRight">Right-side entity. Must carry a <c>[Table]</c> attribute.</typeparam>
/// <typeparam name="TJunction">Junction entity. Must carry a <c>[Table]</c> attribute.</typeparam>
public abstract class SqliteLinkRepository<TLeft, TRight, TJunction> : ILinkRepository<TLeft, TRight>
    where TLeft    : RecordBase
    where TRight   : RecordBase
    where TJunction : RecordBase
{
    private static readonly string LeftTableName    = GetTableName<TLeft>();
    private static readonly string RightTableName   = GetTableName<TRight>();
    private static readonly string JunctionTableName = GetTableName<TJunction>();

    private readonly IDbConnectionFactory _factory;
    private readonly SqliteRepository<TLeft>              _leftRepo;
    private readonly SqliteRepository<TRight>             _rightRepo;
    private readonly SqliteRestorableRepository<TJunction> _junctionRepo;

    /// <summary>Initialises with the three standard infrastructure dependencies.</summary>
    protected SqliteLinkRepository(IDbConnectionFactory factory, IAuditWriter auditWriter, ICallerContext callerContext)
    {
        _factory      = factory;
        _leftRepo     = new SqliteRepository<TLeft>(factory, auditWriter, callerContext);
        _rightRepo    = new SqliteRepository<TRight>(factory, auditWriter, callerContext);
        _junctionRepo = new SqliteRestorableRepository<TJunction>(factory, auditWriter, callerContext);
    }

    // ── Abstract members ───────────────────────────────────────────────────────

    /// <summary>Column name in the junction table that references <typeparamref name="TLeft"/>.</summary>
    protected abstract string LeftFkColumn { get; }

    /// <summary>Column name in the junction table that references <typeparamref name="TRight"/>.</summary>
    protected abstract string RightFkColumn { get; }

    /// <summary>Creates a new, unsaved junction row linking the two IDs.</summary>
    protected abstract TJunction CreateJunction(Guid leftId, Guid rightId);

    // ── ILinkRepository ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task LinkAsync(Guid leftId, Guid rightId, IUnitOfWork? unitOfWork = null)
    {
        var existing = await QueryJunctionAsync(leftId, rightId, unitOfWork);

        if (existing is null)
        {
            await _junctionRepo.InsertAsync(CreateJunction(leftId, rightId), unitOfWork);
        }
        else if (existing.IsDeleted)
        {
            await _junctionRepo.RestoreAsync(existing.Id, unitOfWork);
        }
        // else already linked — no-op
    }

    /// <inheritdoc/>
    public async Task UnlinkAsync(Guid leftId, Guid rightId, IUnitOfWork? unitOfWork = null)
    {
        var existing = await QueryJunctionAsync(leftId, rightId, unitOfWork);

        if (existing is { IsDeleted: false })
            await _junctionRepo.SoftDeleteAsync(existing.Id, unitOfWork);
    }

    /// <inheritdoc/>
    public async Task RestoreLinkAsync(Guid leftId, Guid rightId, IUnitOfWork? unitOfWork = null)
    {
        var existing = await QueryJunctionAsync(leftId, rightId, unitOfWork);

        if (existing is { IsDeleted: true })
            await _junctionRepo.RestoreAsync(existing.Id, unitOfWork);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TRight>> GetRightAsync(Guid leftId, IUnitOfWork? unitOfWork = null)
    {
        var junctionRows = await QueryActiveByFkAsync(JunctionTableName, LeftFkColumn, leftId, unitOfWork);
        if (junctionRows.Count == 0)
            return [];

        var rightIds = ExtractIds(junctionRows, RightFkColumn);
        return await QueryByIdsAsync<TRight>(RightTableName, rightIds, unitOfWork);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TLeft>> GetLeftAsync(Guid rightId, IUnitOfWork? unitOfWork = null)
    {
        var junctionRows = await QueryActiveByFkAsync(JunctionTableName, RightFkColumn, rightId, unitOfWork);
        if (junctionRows.Count == 0)
            return [];

        var leftIds = ExtractIds(junctionRows, LeftFkColumn);
        return await QueryByIdsAsync<TLeft>(LeftTableName, leftIds, unitOfWork);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private async Task<TJunction?> QueryJunctionAsync(Guid leftId, Guid rightId, IUnitOfWork? unitOfWork)
    {
        var param = new
        {
            leftId  = leftId.ToString("D").ToUpperInvariant(),
            rightId = rightId.ToString("D").ToUpperInvariant()
        };
        var sql = RepositorySql.SelectJunctionRow(JunctionTableName, LeftFkColumn, RightFkColumn);

        if (unitOfWork is SqliteUnitOfWork uow)
        {
            var rows = await uow.Connection.QueryAsync<TJunction>(sql, param, uow.Transaction);
            return rows.FirstOrDefault();
        }
        using var conn = _factory.CreateConnection();
        conn.Open();
        var result = await conn.QueryAsync<TJunction>(sql, param);
        return result.FirstOrDefault();
    }

    private async Task<IReadOnlyList<TJunction>> QueryActiveByFkAsync(
        string tableName, string fkColumn, Guid id, IUnitOfWork? unitOfWork)
    {
        var param = new { parentId = id.ToString("D").ToUpperInvariant() };
        var sql   = RepositorySql.SelectByForeignKey(tableName, fkColumn);

        if (unitOfWork is SqliteUnitOfWork uow)
        {
            var rows = await uow.Connection.QueryAsync<TJunction>(sql, param, uow.Transaction);
            return rows.ToList();
        }
        using var conn = _factory.CreateConnection();
        conn.Open();
        var result = await conn.QueryAsync<TJunction>(sql, param);
        return result.ToList();
    }

    private async Task<IReadOnlyList<TEntity>> QueryByIdsAsync<TEntity>(
        string tableName, IEnumerable<string> ids, IUnitOfWork? unitOfWork)
        where TEntity : RecordBase
    {
        var param = new { ids };
        var sql   = RepositorySql.SelectByIds(tableName);

        if (unitOfWork is SqliteUnitOfWork uow)
        {
            var rows = await uow.Connection.QueryAsync<TEntity>(sql, param, uow.Transaction);
            return rows.ToList();
        }
        using var conn = _factory.CreateConnection();
        conn.Open();
        var result = await conn.QueryAsync<TEntity>(sql, param);
        return result.ToList();
    }

    private static IEnumerable<string> ExtractIds(IReadOnlyList<TJunction> rows, string propertyName)
    {
        var prop = typeof(TJunction).GetProperty(propertyName,
            BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                $"Property '{propertyName}' not found on {typeof(TJunction).Name}.");
        return rows.Select(r => (string)prop.GetValue(r)!);
    }

    private static string GetTableName<T>()
        => typeof(T).GetCustomAttribute<TableAttribute>()?.Name
           ?? throw new InvalidOperationException(
               $"{typeof(T).Name} must carry a [Table] attribute.");
}

using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>In-memory test double for <see cref="IImportBatchRepository"/> — avoids requiring a real database in endpoint tests.</summary>
internal sealed class FakeImportBatchRepository : IImportBatchRepository
{
    private readonly Dictionary<Guid, ImportBatchEntity> _batches = new();

    /// <summary>Registers a fixed batch for a test to look up.</summary>
    public void Seed(ImportBatchEntity batch) => _batches[batch.Id] = batch;

    public Task<ImportBatchEntity?> GetByIdAsync(Guid id, IUnitOfWork? unitOfWork = null)
        => Task.FromResult(_batches.GetValueOrDefault(id));

    public Task<IReadOnlyList<ImportBatchEntity>> GetAllAsync(IUnitOfWork? unitOfWork = null)
        => Task.FromResult<IReadOnlyList<ImportBatchEntity>>(_batches.Values.ToList());

    public Task<IReadOnlyList<ImportBatchEntity>> GetByTypeAsync(ImportBatchType type, IUnitOfWork? unitOfWork = null)
        => Task.FromResult<IReadOnlyList<ImportBatchEntity>>(_batches.Values.Where(b => b.Type.Parsed == type).ToList());

    public Task<PagedItems<ImportBatchEntity>> GetPagedAsync(ImportBatchType? type, ImportBatchStatus? status, int page, int pageSize)
    {
        var filtered = _batches.Values.AsEnumerable();
        if (type is not null)   filtered = filtered.Where(b => b.Type.Parsed == type);
        if (status is not null) filtered = filtered.Where(b => b.Status.Parsed == status);

        var ordered = filtered.OrderByDescending(b => b.ImportedAt, StringComparer.OrdinalIgnoreCase).ToList();

        var total             = ordered.Count;
        var effectivePageSize = pageSize == 0 ? total : pageSize;
        var items             = pageSize == 0 ? ordered : ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Task.FromResult(new PagedItems<ImportBatchEntity>(items, page, effectivePageSize, total));
    }

    public Task UpdateRecordCountAsync(Guid id, int count, IUnitOfWork? unitOfWork = null)
    {
        if (_batches.TryGetValue(id, out var batch)) batch.RecordCount = count;
        return Task.CompletedTask;
    }

    public Task InsertAsync(ImportBatchEntity entity, IUnitOfWork? unitOfWork = null)
    {
        _batches[entity.Id] = entity;
        return Task.CompletedTask;
    }

    public Task InsertManyAsync(IEnumerable<ImportBatchEntity> entities, IUnitOfWork? unitOfWork = null, InsertStrategy strategy = InsertStrategy.Bulk)
    {
        foreach (var entity in entities) _batches[entity.Id] = entity;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ImportBatchEntity entity, IUnitOfWork? unitOfWork = null)
    {
        _batches[entity.Id] = entity;
        return Task.CompletedTask;
    }

    public Task SoftDeleteAsync(Guid id, IUnitOfWork? unitOfWork = null)
    {
        _batches.Remove(id);
        return Task.CompletedTask;
    }
}

using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Core.Entities;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>In-memory <see cref="IListableRepository{T}"/> double for <see cref="SeriesEntity"/>, seeded via
/// the constructor so tests can construct it with known fixtures. Mirrors the real repository's documented
/// <c>pageSize = 0</c>/effective-size contract so it cannot silently diverge from #195's behaviour.</summary>
internal sealed class FakeSeriesRepository : IListableRepository<SeriesEntity>
{
    private readonly List<SeriesEntity> _series;

    internal FakeSeriesRepository(IEnumerable<SeriesEntity>? seed = null)
    {
        _series = seed?.ToList() ?? [];
    }

    public Task<PagedItems<SeriesEntity>> GetPageAsync(
        int page, int pageSize, IReadOnlyList<SortColumn>? orderBy = null, IUnitOfWork? unitOfWork = null)
    {
        var active = _series.Where(s => !s.IsDeleted).OrderBy(s => s.DateCreated.Parsed).ToList();

        var items = pageSize == 0
            ? active
            : active.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var effectivePageSize = pageSize == 0 ? items.Count : pageSize;
        return Task.FromResult(new PagedItems<SeriesEntity>(items, page, effectivePageSize, active.Count));
    }

    public Task<SeriesEntity?> GetByIdAsync(Guid id, IUnitOfWork? unitOfWork = null)
        => Task.FromResult(_series.FirstOrDefault(s => s.Id == id && !s.IsDeleted));

    public Task InsertAsync(SeriesEntity entity, IUnitOfWork? unitOfWork = null)
    {
        _series.Add(entity);
        return Task.CompletedTask;
    }

    public Task InsertManyAsync(IEnumerable<SeriesEntity> entities, IUnitOfWork? unitOfWork = null, InsertStrategy strategy = InsertStrategy.Bulk)
    {
        _series.AddRange(entities);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(SeriesEntity entity, IUnitOfWork? unitOfWork = null)
    {
        var index = _series.FindIndex(s => s.Id == entity.Id);
        if (index >= 0)
            _series[index] = entity;
        return Task.CompletedTask;
    }

    public Task SoftDeleteAsync(Guid id, IUnitOfWork? unitOfWork = null)
    {
        var index = _series.FindIndex(s => s.Id == id);
        if (index >= 0)
            _series[index] = new SeriesEntity
            {
                Id                  = _series[index].Id,
                Name                = _series[index].Name,
                UniverseId          = _series[index].UniverseId,
                ImportBatchId       = _series[index].ImportBatchId,
                CompletenessStatus  = _series[index].CompletenessStatus,
                NoValueKnown        = _series[index].NoValueKnown,
                DateCreated         = _series[index].DateCreated,
                DateModified        = SafeDateValue.Now,
                DateDeleted         = SafeDateValue.Now,
                IsDeleted           = true,
            };
        return Task.CompletedTask;
    }
}

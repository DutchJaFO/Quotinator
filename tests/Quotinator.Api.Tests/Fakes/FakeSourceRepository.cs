using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Core.Entities;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>In-memory <see cref="IListableRepository{T}"/> double for <see cref="Source"/>, seeded via the
/// constructor so tests can construct it with known fixtures. Mirrors the real repository's documented
/// <c>pageSize = 0</c>/effective-size contract so it cannot silently diverge from #195's behaviour.</summary>
internal sealed class FakeSourceRepository : IListableRepository<Source>
{
    private readonly List<Source> _sources;

    internal FakeSourceRepository(IEnumerable<Source>? seed = null)
    {
        _sources = seed?.ToList() ?? [];
    }

    public Task<PagedItems<Source>> GetPageAsync(
        int page, int pageSize, IReadOnlyList<SortColumn>? orderBy = null, IUnitOfWork? unitOfWork = null)
    {
        var active = _sources.Where(s => !s.IsDeleted).OrderBy(s => s.DateCreated.Parsed).ToList();

        var items = pageSize == 0
            ? active
            : active.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var effectivePageSize = pageSize == 0 ? items.Count : pageSize;
        return Task.FromResult(new PagedItems<Source>(items, page, effectivePageSize, active.Count));
    }

    public Task<Source?> GetByIdAsync(Guid id, IUnitOfWork? unitOfWork = null)
        => Task.FromResult(_sources.FirstOrDefault(s => s.Id == id && !s.IsDeleted));

    public Task InsertAsync(Source entity, IUnitOfWork? unitOfWork = null)
    {
        _sources.Add(entity);
        return Task.CompletedTask;
    }

    public Task InsertManyAsync(IEnumerable<Source> entities, IUnitOfWork? unitOfWork = null, InsertStrategy strategy = InsertStrategy.Bulk)
    {
        _sources.AddRange(entities);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Source entity, IUnitOfWork? unitOfWork = null)
    {
        var index = _sources.FindIndex(s => s.Id == entity.Id);
        if (index >= 0)
            _sources[index] = entity;
        return Task.CompletedTask;
    }

    public Task SoftDeleteAsync(Guid id, IUnitOfWork? unitOfWork = null)
    {
        var index = _sources.FindIndex(s => s.Id == id);
        if (index >= 0)
            _sources[index] = new Source
            {
                Id                  = _sources[index].Id,
                Title               = _sources[index].Title,
                Type                = _sources[index].Type,
                Date                = _sources[index].Date,
                SeriesId            = _sources[index].SeriesId,
                ImportBatchId       = _sources[index].ImportBatchId,
                CompletenessStatus  = _sources[index].CompletenessStatus,
                NoValueKnown        = _sources[index].NoValueKnown,
                DateCreated         = _sources[index].DateCreated,
                DateModified        = SafeDateValue.Now,
                DateDeleted         = SafeDateValue.Now,
                IsDeleted           = true,
            };
        return Task.CompletedTask;
    }
}

using Quotinator.Data.Enums;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Core.Entities;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>In-memory <see cref="IListableRepository{T}"/> double for <see cref="SeasonEntity"/>, seeded via
/// the constructor so tests can construct it with known fixtures. Mirrors the real repository's documented
/// <c>pageSize = 0</c>/effective-size contract so it cannot silently diverge from #195's behaviour.</summary>
internal sealed class FakeSeasonRepository : IListableRepository<SeasonEntity>
{
    private readonly List<SeasonEntity> _seasons;

    internal FakeSeasonRepository(IEnumerable<SeasonEntity>? seed = null)
    {
        _seasons = seed?.ToList() ?? [];
    }

    public Task<PagedItems<SeasonEntity>> GetPageAsync(
        int page, int pageSize, IReadOnlyList<SortColumn>? orderBy = null, IUnitOfWork? unitOfWork = null)
    {
        List<SeasonEntity> active = [.. _seasons.Where(s => !s.IsDeleted).OrderBy(s => s.DateCreated.Parsed)];

        List<SeasonEntity> items = pageSize == 0
            ? active
            : [.. active.Skip((page - 1) * pageSize).Take(pageSize)];

        int effectivePageSize = pageSize == 0 ? items.Count : pageSize;
        return Task.FromResult(new PagedItems<SeasonEntity>(items, page, effectivePageSize, active.Count));
    }

    public Task<SeasonEntity?> GetByIdAsync(Guid id, IUnitOfWork? unitOfWork = null)
        => Task.FromResult(_seasons.FirstOrDefault(s => s.Id == id && !s.IsDeleted));

    public Task InsertAsync(SeasonEntity entity, IUnitOfWork? unitOfWork = null)
    {
        _seasons.Add(entity);
        return Task.CompletedTask;
    }

    public Task InsertManyAsync(IEnumerable<SeasonEntity> entities, IUnitOfWork? unitOfWork = null, InsertStrategy strategy = InsertStrategy.Bulk)
    {
        _seasons.AddRange(entities);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(SeasonEntity entity, IUnitOfWork? unitOfWork = null)
    {
        int index = _seasons.FindIndex(s => s.Id == entity.Id);
        if (index >= 0)
            _seasons[index] = entity;
        return Task.CompletedTask;
    }

    public Task SoftDeleteAsync(Guid id, IUnitOfWork? unitOfWork = null)
    {
        int index = _seasons.FindIndex(s => s.Id == id);
        if (index >= 0)
            _seasons[index] = new SeasonEntity
            {
                Id                  = _seasons[index].Id,
                Number              = _seasons[index].Number,
                Title               = _seasons[index].Title,
                Subtitle            = _seasons[index].Subtitle,
                SeriesId            = _seasons[index].SeriesId,
                ImportBatchId       = _seasons[index].ImportBatchId,
                CompletenessStatus  = _seasons[index].CompletenessStatus,
                NoValueKnown        = _seasons[index].NoValueKnown,
                DateCreated         = _seasons[index].DateCreated,
                DateModified        = SafeDateValue.Now,
                DateDeleted         = SafeDateValue.Now,
                IsDeleted           = true,
            };
        return Task.CompletedTask;
    }
}

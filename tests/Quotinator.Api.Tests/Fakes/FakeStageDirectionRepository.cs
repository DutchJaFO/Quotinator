using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Core.Entities;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>
/// Test double for <see cref="IListableRepository{T}"/> over <see cref="StageDirectionEntity"/> — returns
/// a canned page or a canned single entity, recording the arguments it was called with. Write methods are
/// not needed by any StageDirection endpoint today and throw if exercised, so a test that accidentally
/// reaches one fails loudly instead of silently succeeding.
/// </summary>
internal sealed class FakeStageDirectionRepository : IListableRepository<StageDirectionEntity>
{
    public PagedItems<StageDirectionEntity>? ReturnPage { get; set; }
    public StageDirectionEntity? ReturnById { get; set; }

    public int? LastPageRequested { get; private set; }
    public int? LastPageSizeRequested { get; private set; }
    public Guid? LastIdRequested { get; private set; }

    public Task<PagedItems<StageDirectionEntity>> GetPageAsync(
        int page, int pageSize, IReadOnlyList<SortColumn>? orderBy = null, IUnitOfWork? unitOfWork = null)
    {
        LastPageRequested     = page;
        LastPageSizeRequested = pageSize;
        return Task.FromResult(ReturnPage ?? new PagedItems<StageDirectionEntity>([], page, pageSize, 0));
    }

    public Task<StageDirectionEntity?> GetByIdAsync(Guid id, IUnitOfWork? unitOfWork = null)
    {
        LastIdRequested = id;
        return Task.FromResult(ReturnById is not null && ReturnById.Id == id ? ReturnById : null);
    }

    public Task InsertAsync(StageDirectionEntity entity, IUnitOfWork? unitOfWork = null) => throw new NotImplementedException();

    public Task InsertManyAsync(IEnumerable<StageDirectionEntity> entities, IUnitOfWork? unitOfWork = null, InsertStrategy strategy = InsertStrategy.Bulk)
        => throw new NotImplementedException();

    public Task UpdateAsync(StageDirectionEntity entity, IUnitOfWork? unitOfWork = null) => throw new NotImplementedException();

    public Task SoftDeleteAsync(Guid id, IUnitOfWork? unitOfWork = null) => throw new NotImplementedException();
}

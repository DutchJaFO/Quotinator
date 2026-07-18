using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Engine.Entities;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>
/// Test double for <see cref="IListableRepository{T}"/> over <see cref="UniverseEntity"/> — returns a
/// canned page or a canned single entity, recording the arguments it was called with. Write methods are
/// not needed by any Universe endpoint today and throw if exercised, so a test that accidentally reaches
/// one fails loudly instead of silently succeeding.
/// </summary>
internal sealed class FakeUniverseRepository : IListableRepository<UniverseEntity>
{
    public PagedItems<UniverseEntity>? ReturnPage { get; set; }
    public UniverseEntity? ReturnById { get; set; }

    public int? LastPageRequested { get; private set; }
    public int? LastPageSizeRequested { get; private set; }
    public Guid? LastIdRequested { get; private set; }

    public Task<PagedItems<UniverseEntity>> GetPageAsync(
        int page, int pageSize, IReadOnlyList<SortColumn>? orderBy = null, IUnitOfWork? unitOfWork = null)
    {
        LastPageRequested     = page;
        LastPageSizeRequested = pageSize;
        return Task.FromResult(ReturnPage ?? new PagedItems<UniverseEntity>([], page, pageSize, 0));
    }

    public Task<UniverseEntity?> GetByIdAsync(Guid id, IUnitOfWork? unitOfWork = null)
    {
        LastIdRequested = id;
        return Task.FromResult(ReturnById is not null && ReturnById.Id == id ? ReturnById : null);
    }

    public Task InsertAsync(UniverseEntity entity, IUnitOfWork? unitOfWork = null) => throw new NotImplementedException();

    public Task InsertManyAsync(IEnumerable<UniverseEntity> entities, IUnitOfWork? unitOfWork = null, InsertStrategy strategy = InsertStrategy.Bulk)
        => throw new NotImplementedException();

    public Task UpdateAsync(UniverseEntity entity, IUnitOfWork? unitOfWork = null) => throw new NotImplementedException();

    public Task SoftDeleteAsync(Guid id, IUnitOfWork? unitOfWork = null) => throw new NotImplementedException();
}

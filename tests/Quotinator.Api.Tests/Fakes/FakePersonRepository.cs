using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Engine.Entities;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>
/// Test double for <see cref="IListableRepository{T}"/> over <see cref="Person"/> — returns a canned
/// page or a canned single entity, recording the arguments it was called with. Write methods are not
/// needed by any Person endpoint today and throw if exercised, so a test that accidentally reaches one
/// fails loudly instead of silently succeeding.
/// </summary>
internal sealed class FakePersonRepository : IListableRepository<Person>
{
    public PagedItems<Person>? ReturnPage { get; set; }
    public Person? ReturnById { get; set; }

    public int? LastPageRequested { get; private set; }
    public int? LastPageSizeRequested { get; private set; }
    public Guid? LastIdRequested { get; private set; }

    public Task<PagedItems<Person>> GetPageAsync(
        int page, int pageSize, IReadOnlyList<SortColumn>? orderBy = null, IUnitOfWork? unitOfWork = null)
    {
        LastPageRequested     = page;
        LastPageSizeRequested = pageSize;
        return Task.FromResult(ReturnPage ?? new PagedItems<Person>([], page, pageSize, 0));
    }

    public Task<Person?> GetByIdAsync(Guid id, IUnitOfWork? unitOfWork = null)
    {
        LastIdRequested = id;
        return Task.FromResult(ReturnById is not null && ReturnById.Id == id ? ReturnById : null);
    }

    public Task InsertAsync(Person entity, IUnitOfWork? unitOfWork = null) => throw new NotImplementedException();

    public Task InsertManyAsync(IEnumerable<Person> entities, IUnitOfWork? unitOfWork = null, InsertStrategy strategy = InsertStrategy.Bulk)
        => throw new NotImplementedException();

    public Task UpdateAsync(Person entity, IUnitOfWork? unitOfWork = null) => throw new NotImplementedException();

    public Task SoftDeleteAsync(Guid id, IUnitOfWork? unitOfWork = null) => throw new NotImplementedException();
}

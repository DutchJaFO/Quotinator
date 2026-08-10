using Quotinator.Data.Enums;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Core.Entities;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>In-memory <see cref="IListableRepository{T}"/> double for <see cref="CharacterEntity"/>, seeded via the
/// constructor so tests can construct it with known fixtures. Mirrors the real repository's documented
/// <c>pageSize = 0</c>/effective-size contract so it cannot silently diverge from #195's behaviour.</summary>
internal sealed class FakeCharacterRepository : IListableRepository<CharacterEntity>
{
    private readonly List<CharacterEntity> _characters;

    internal FakeCharacterRepository(IEnumerable<CharacterEntity>? seed = null)
    {
        _characters = seed?.ToList() ?? [];
    }

    public Task<PagedItems<CharacterEntity>> GetPageAsync(
        int page, int pageSize, IReadOnlyList<SortColumn>? orderBy = null, IUnitOfWork? unitOfWork = null)
    {
        var active = _characters.Where(c => !c.IsDeleted).OrderBy(c => c.DateCreated.Parsed).ToList();

        var items = pageSize == 0
            ? active
            : [.. active.Skip((page - 1) * pageSize).Take(pageSize)];

        var effectivePageSize = pageSize == 0 ? items.Count : pageSize;
        return Task.FromResult(new PagedItems<CharacterEntity>(items, page, effectivePageSize, active.Count));
    }

    public Task<CharacterEntity?> GetByIdAsync(Guid id, IUnitOfWork? unitOfWork = null)
        => Task.FromResult(_characters.FirstOrDefault(c => c.Id == id && !c.IsDeleted));

    public Task InsertAsync(CharacterEntity entity, IUnitOfWork? unitOfWork = null)
    {
        _characters.Add(entity);
        return Task.CompletedTask;
    }

    public Task InsertManyAsync(IEnumerable<CharacterEntity> entities, IUnitOfWork? unitOfWork = null, InsertStrategy strategy = InsertStrategy.Bulk)
    {
        _characters.AddRange(entities);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(CharacterEntity entity, IUnitOfWork? unitOfWork = null)
    {
        var index = _characters.FindIndex(c => c.Id == entity.Id);
        if (index >= 0)
            _characters[index] = entity;
        return Task.CompletedTask;
    }

    public Task SoftDeleteAsync(Guid id, IUnitOfWork? unitOfWork = null)
    {
        var index = _characters.FindIndex(c => c.Id == id);
        if (index >= 0)
            _characters[index] = new CharacterEntity
            {
                Id                  = _characters[index].Id,
                Name                = _characters[index].Name,
                ImportBatchId       = _characters[index].ImportBatchId,
                CompletenessStatus  = _characters[index].CompletenessStatus,
                NoValueKnown        = _characters[index].NoValueKnown,
                DateCreated         = _characters[index].DateCreated,
                DateModified        = SafeDateValue.Now,
                DateDeleted         = SafeDateValue.Now,
                IsDeleted           = true,
            };
        return Task.CompletedTask;
    }
}

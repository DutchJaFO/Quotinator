using Quotinator.Data.Models;
using Quotinator.Data.Repositories;
using Quotinator.Core.Entities;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>In-memory <see cref="IListableRepository{T}"/> double for <see cref="ConversationEntity"/>,
/// seeded via the constructor so tests can construct it with known fixtures. Mirrors the real repository's
/// documented <c>pageSize = 0</c>/effective-size contract so it cannot silently diverge from #195's
/// behaviour.</summary>
internal sealed class FakeConversationRepository : IListableRepository<ConversationEntity>
{
    private readonly List<ConversationEntity> _conversations;

    internal FakeConversationRepository(IEnumerable<ConversationEntity>? seed = null)
    {
        _conversations = seed?.ToList() ?? [];
    }

    public Task<PagedItems<ConversationEntity>> GetPageAsync(
        int page, int pageSize, IReadOnlyList<SortColumn>? orderBy = null, IUnitOfWork? unitOfWork = null)
    {
        var active = _conversations.Where(c => !c.IsDeleted).OrderBy(c => c.DateCreated.Parsed).ToList();

        var items = pageSize == 0
            ? active
            : active.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var effectivePageSize = pageSize == 0 ? items.Count : pageSize;
        return Task.FromResult(new PagedItems<ConversationEntity>(items, page, effectivePageSize, active.Count));
    }

    public Task<ConversationEntity?> GetByIdAsync(Guid id, IUnitOfWork? unitOfWork = null)
        => Task.FromResult(_conversations.FirstOrDefault(c => c.Id == id && !c.IsDeleted));

    public Task InsertAsync(ConversationEntity entity, IUnitOfWork? unitOfWork = null)
    {
        _conversations.Add(entity);
        return Task.CompletedTask;
    }

    public Task InsertManyAsync(IEnumerable<ConversationEntity> entities, IUnitOfWork? unitOfWork = null, InsertStrategy strategy = InsertStrategy.Bulk)
    {
        _conversations.AddRange(entities);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ConversationEntity entity, IUnitOfWork? unitOfWork = null)
    {
        var index = _conversations.FindIndex(c => c.Id == entity.Id);
        if (index >= 0)
            _conversations[index] = entity;
        return Task.CompletedTask;
    }

    public Task SoftDeleteAsync(Guid id, IUnitOfWork? unitOfWork = null)
    {
        var index = _conversations.FindIndex(c => c.Id == id);
        if (index >= 0)
            _conversations[index] = new ConversationEntity
            {
                Id                  = _conversations[index].Id,
                Description         = _conversations[index].Description,
                ImportBatchId       = _conversations[index].ImportBatchId,
                CompletenessStatus  = _conversations[index].CompletenessStatus,
                NoValueKnown        = _conversations[index].NoValueKnown,
                DateCreated         = _conversations[index].DateCreated,
                DateModified        = SafeDateValue.Now,
                DateDeleted         = SafeDateValue.Now,
                IsDeleted           = true,
            };
        return Task.CompletedTask;
    }
}

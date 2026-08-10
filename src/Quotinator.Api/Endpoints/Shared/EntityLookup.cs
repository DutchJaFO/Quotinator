using Quotinator.Core.Services;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Api.Endpoints.Shared;

/// <summary>
/// Shared <c>GetById</c> idiom for the masterdata entities that use the plain repository pattern —
/// extracted from #281's investigation: <c>Guid.TryParse → repo.GetByIdAsync → null-or-mapped →
/// 404-or-200</c>, previously duplicated across 7 endpoint files in two inconsistent code shapes.
/// Not used by <c>ConversationEndpoints</c>, whose <c>GetById</c> hydrates a multi-table aggregate via
/// a different mechanism (ADR 017/#285), not <see cref="IRepository{T}.GetByIdAsync"/>.
/// </summary>
internal static class EntityLookup
{
    /// <summary>
    /// Parses <paramref name="id"/>, looks it up via <paramref name="repository"/>, and maps a found
    /// entity through <paramref name="mapAsync"/> — returning a 404 (keyed by
    /// <paramref name="notFoundMessageKey"/>) for a malformed id or a missing entity, otherwise 200.
    /// </summary>
    internal static async Task<IResult> TryFindByIdAsync<TEntity, TResponse>(
        string id, IApiLocalizer localizer, IRepository<TEntity> repository,
        string notFoundMessageKey, Func<TEntity, Task<TResponse>> mapAsync)
        where TEntity : RecordBase
        where TResponse : class
    {
        if (!Guid.TryParse(id, out var parsedId))
            return NotFoundResult.OkOrNotFound<TResponse>(null, localizer, notFoundMessageKey);

        var entity = await repository.GetByIdAsync(parsedId);
        if (entity is null)
            return NotFoundResult.OkOrNotFound<TResponse>(null, localizer, notFoundMessageKey);

        var response = await mapAsync(entity);
        return NotFoundResult.OkOrNotFound(response, localizer, notFoundMessageKey);
    }
}

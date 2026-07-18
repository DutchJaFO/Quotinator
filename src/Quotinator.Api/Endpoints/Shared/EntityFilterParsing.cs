using Quotinator.Constants.Api;
using Quotinator.Core.Services;

namespace Quotinator.Api.Endpoints.Shared;

/// <summary>Which of the two mutually-exclusive filter forms <see cref="EntityFilterParsing.ResolveAsync"/> resolved to.</summary>
internal enum EntityFilterOutcome
{
    /// <summary>Neither the id-valued nor the name-valued parameter was supplied.</summary>
    NoFilter,

    /// <summary>An id was resolved — either supplied directly or found by name.</summary>
    Resolved,

    /// <summary>A name-valued filter was supplied but no matching entity exists — a legitimate zero-results case, not an error.</summary>
    NotFound,

    /// <summary>Both parameters were supplied, or the id-valued one was malformed.</summary>
    Error,
}

/// <summary>The parameter and entity names used to build <see cref="EntityFilterParsing.ResolveAsync"/>'s localised messages.</summary>
internal readonly record struct EntityFilterNames(string EntityType, string IdParam, string NameParam);

/// <summary>The outcome of resolving one entity-scoped filter — see <see cref="EntityFilterParsing.ResolveAsync"/>.</summary>
internal readonly record struct EntityFilterResult(EntityFilterOutcome Outcome, Guid? Id, string? Message, IResult? Error);

/// <summary>
/// Shared entity-scoped filter resolution for masterdata-consuming endpoints — implements #196's
/// convention. Not wired to a real repository by this issue; a consumer supplies its own
/// <c>resolveIdByName</c> delegate.
/// </summary>
internal static class EntityFilterParsing
{
    /// <summary>
    /// Resolves the mutually-exclusive <paramref name="idValue"/>/<paramref name="nameValue"/> pair to a
    /// single id. <paramref name="nameValue"/> is resolved via <paramref name="resolveIdByName"/> rather
    /// than applied as a direct contains-match — a name that resolves to nothing already means zero
    /// possible results, reported as <see cref="EntityFilterOutcome.NotFound"/> (not an error) so the
    /// caller can respond informatively without running a query that would also come back empty.
    /// </summary>
    internal static async Task<EntityFilterResult> ResolveAsync(
        string? idValue, string? nameValue, EntityFilterNames names,
        Func<string, Task<Guid?>> resolveIdByName, IApiLocalizer localizer)
    {
        if (idValue is not null && nameValue is not null)
            return new EntityFilterResult(EntityFilterOutcome.Error, null, null, Results.Problem(
                detail: string.Format(localizer[ApiMessages.MutuallyExclusiveEntityFilter], names.IdParam, names.NameParam),
                statusCode: StatusCodes.Status422UnprocessableEntity));

        if (idValue is not null)
        {
            if (!Guid.TryParse(idValue, out var parsed))
                return new EntityFilterResult(EntityFilterOutcome.Error, null, null, Results.Problem(
                    detail: string.Format(localizer[ApiMessages.InvalidEntityFilterId], names.IdParam),
                    statusCode: StatusCodes.Status422UnprocessableEntity));

            return new EntityFilterResult(EntityFilterOutcome.Resolved, parsed, null, null);
        }

        if (nameValue is not null)
        {
            var resolvedId = await resolveIdByName(nameValue);
            return resolvedId is null
                ? new EntityFilterResult(EntityFilterOutcome.NotFound, null,
                    string.Format(localizer[ApiMessages.EntityFilterNoMatch], names.EntityType, nameValue), null)
                : new EntityFilterResult(EntityFilterOutcome.Resolved, resolvedId, null, null);
        }

        return new EntityFilterResult(EntityFilterOutcome.NoFilter, null, null, null);
    }
}

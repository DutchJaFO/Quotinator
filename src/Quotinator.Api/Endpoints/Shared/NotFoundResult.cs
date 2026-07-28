using Quotinator.Core.Services;

namespace Quotinator.Api.Endpoints.Shared;

/// <summary>Shared 404-or-200 result for a <c>GetById</c>-style lookup — extracted from <see cref="Quotinator.Api.Endpoints.QuoteEndpoints"/> and <see cref="Quotinator.Api.Endpoints.ConversationEndpoints"/>, which had identical duplicated logic.</summary>
internal static class NotFoundResult
{
    /// <summary>Returns 404 with <paramref name="notFoundMessageKey"/>'s message when <paramref name="entity"/> is <see langword="null"/>, otherwise 200 with the entity.</summary>
    internal static IResult OkOrNotFound<T>(T? entity, IApiLocalizer localizer, string notFoundMessageKey) where T : class
        => entity is null
            ? Results.Problem(
                detail: localizer[notFoundMessageKey],
                statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(entity);
}

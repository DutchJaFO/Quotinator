namespace Quotinator.Api.OpenApi;

/// <summary>Normalises an operation's relative path for lookup against a path-keyed schema transformer registry.</summary>
/// <remarks>
/// <c>context.Description.RelativePath</c> for a group's bare listing endpoint (e.g.
/// <c>group.MapGet("/", GetAll)</c>) reports a trailing slash (<c>"api/v1/quotes/"</c>) that sibling
/// routes on the same group don't have. Missing this trim once silently disabled
/// <see cref="NumericParameterSchemaTransformer"/> for <c>GET /api/v1/quotes</c> — see its remarks.
/// </remarks>
internal static class ScopedPath
{
    /// <summary>Returns <paramref name="relativePath"/> with any trailing slash removed, or the empty string when <see langword="null"/>.</summary>
    public static string From(string? relativePath) => (relativePath ?? string.Empty).TrimEnd('/');
}

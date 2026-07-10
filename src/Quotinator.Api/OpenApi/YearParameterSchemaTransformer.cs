using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Quotinator.Api.OpenApi;

/// <summary>Patches year/decade query parameters to <c>integer</c> in the OpenAPI spec.</summary>
/// <remarks>
/// <para>
/// <c>yearFrom</c>, <c>yearTo</c>, <c>year</c>, and <c>decade</c> are declared as <c>string?</c>
/// in handler signatures so that invalid input (e.g. <c>yearFrom=1980x</c>) is caught by
/// <c>TryParseYear()</c> at the point of origin and returned as 422, rather than propagating as
/// an unhandled <see cref="BadHttpRequestException"/> through the entire middleware stack.
/// </para>
/// <para>
/// The downside is that the OpenAPI generator infers <c>type: string</c> from the C# type.
/// This transformer patches only the three quote-filter endpoints back to <c>type: integer</c>,
/// keeping the spec accurate without reverting the error-handling approach.
/// </para>
/// <para>
/// Scoped to the three paths that use <c>TryParseYear</c>. Do not add any endpoint to
/// <see cref="YearFilterPaths"/> unless it also uses <c>TryParseYear</c> for these parameters.
/// </para>
/// <para>
/// <c>context.Description.RelativePath</c> for the bare listing endpoint
/// (<c>group.MapGet("/", GetAll)</c>) is <c>"api/v1/quotes/"</c> — a trailing slash the other two
/// paths don't have — so the match trims it before comparing against <see cref="YearFilterPaths"/>'s
/// slash-free keys. Missing this silently meant <c>GET /api/v1/quotes</c>'s year filters were never
/// actually patched to <c>integer</c>, despite this transformer existing — found while diagnosing
/// <see cref="EnumParameterSchemaTransformer"/> against the live spec, not by any prior test.
/// </para>
/// </remarks>
internal sealed class YearParameterSchemaTransformer : IOpenApiOperationTransformer
{
    internal static readonly HashSet<string> YearFilterPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "api/v1/quotes",
        "api/v1/quotes/random",
        "api/v1/quotes/search",
    };

    internal static readonly HashSet<string> YearParamNames = new(StringComparer.OrdinalIgnoreCase)
        { "yearFrom", "yearTo", "year", "decade" };

    /// <inheritdoc/>
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var path = (context.Description.RelativePath ?? string.Empty).TrimEnd('/');
        if (!YearFilterPaths.Contains(path))
            return Task.CompletedTask;

        foreach (var param in operation.Parameters ?? [])
        {
            if (param.Name is not null && YearParamNames.Contains(param.Name)
                && param is OpenApiParameter p && p.Schema is OpenApiSchema s)
                s.Type = JsonSchemaType.Integer | JsonSchemaType.Null;
        }

        return Task.CompletedTask;
    }
}

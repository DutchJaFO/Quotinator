using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Quotinator.Constants.Api;

namespace Quotinator.Api.OpenApi;

/// <summary>Patches numeric query parameters to <c>integer</c> in the OpenAPI spec, publishing their default where one exists.</summary>
/// <remarks>
/// <para>
/// Numeric query parameters that need custom out-of-range handling (e.g. a clean 422 instead of the
/// model binder's bare 400 on <c>page=abc</c>) are declared <c>string?</c> in handler signatures and
/// parsed with <c>int.TryParse</c> — see <c>CLAUDE.md</c>'s "Rules for adding new numeric query
/// parameters". The downside is that the OpenAPI generator infers <c>type: string</c> from the C#
/// type, and any <c>[DefaultValue]</c> attribute is lost along with it (the generator derives the
/// published default from the schema it inferred, not the attribute value). This transformer patches
/// both back: the type to <c>integer</c>, and — for parameters registered with one — the default.
/// </para>
/// <para>
/// Scoped by path <em>and</em> parameter name via <see cref="NumericParamsByPath"/>. Registering a
/// path alone, without also registering the specific parameter names on it, silently patches nothing
/// — this is exactly the gap #194 found: the endpoint paths were registered for the year params, but
/// <c>page</c>/<c>pageSize</c>/<c>n</c>/<c>limit</c> were never added alongside them.
/// </para>
/// <para>
/// See <see cref="ScopedPath"/> for why the operation's relative path is normalised before lookup.
/// </para>
/// </remarks>
internal sealed class NumericParameterSchemaTransformer : IOpenApiOperationTransformer
{
    internal static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, int?>> NumericParamsByPath =
        new Dictionary<string, IReadOnlyDictionary<string, int?>>(StringComparer.OrdinalIgnoreCase)
        {
            ["api/v1/quotes"] = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase)
            {
                ["page"]     = QueryParamDefaults.Page,
                ["pageSize"] = QueryParamDefaults.PageSize,
                ["yearFrom"] = null,
                ["yearTo"]   = null,
                ["year"]     = null,
                ["decade"]   = null,
            },
            ["api/v1/quotes/random"] = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase)
            {
                ["n"]        = QueryParamDefaults.RandomCount,
                ["yearFrom"] = null,
                ["yearTo"]   = null,
                ["year"]     = null,
                ["decade"]   = null,
            },
            ["api/v1/quotes/search"] = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase)
            {
                ["limit"]    = QueryParamDefaults.SearchLimit,
                ["yearFrom"] = null,
                ["yearTo"]   = null,
                ["year"]     = null,
                ["decade"]   = null,
            },
            ["api/v1/admin/audit"] = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase)
            {
                ["page"]     = QueryParamDefaults.Page,
                ["pageSize"] = QueryParamDefaults.PageSize,
            },
            ["api/v1/import/actions"] = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase)
            {
                ["page"]     = QueryParamDefaults.Page,
                ["pageSize"] = QueryParamDefaults.PageSize,
            },
            ["api/v1/masterdata/sources"] = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase)
            {
                ["page"]     = QueryParamDefaults.Page,
                ["pageSize"] = QueryParamDefaults.PageSize,
            },
        };

    /// <inheritdoc/>
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var path = ScopedPath.From(context.Description.RelativePath);
        if (!NumericParamsByPath.TryGetValue(path, out var paramMap))
            return Task.CompletedTask;

        foreach (var param in operation.Parameters ?? [])
        {
            if (param.Name is null || !paramMap.TryGetValue(param.Name, out var @default)
                || param is not OpenApiParameter p || p.Schema is not OpenApiSchema s)
                continue;

            s.Type = JsonSchemaType.Integer | JsonSchemaType.Null;
            if (@default is not null)
                s.Default = JsonValue.Create(@default.Value);
        }

        return Task.CompletedTask;
    }
}

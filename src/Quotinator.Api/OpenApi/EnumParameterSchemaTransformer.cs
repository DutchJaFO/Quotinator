using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Quotinator.Core.Helpers;
using Quotinator.Data.Entities;

namespace Quotinator.Api.OpenApi;

/// <summary>Patches closed-set query parameters to a real OpenAPI <c>enum</c> instead of an unconstrained string.</summary>
/// <remarks>
/// <para>
/// <c>type</c>, <c>field</c>, <c>status</c>, and <c>entityType</c> are validated against a fixed
/// vocabulary but bound as <c>string</c>/<c>string[]</c> in the handler signature — same reasoning
/// as <see cref="YearParameterSchemaTransformer"/>, so the generator infers an unconstrained schema.
/// </para>
/// <para>
/// Values are derived from their actual source of truth where one exists
/// (<see cref="InputValidation.ValidTypes"/>, <see cref="InputValidation.ValidSearchFields"/>, the
/// <see cref="ImportActionStatus"/> enum) so this can never drift out of sync. <c>entityType</c> has
/// no such source — <see cref="SystemImportAction.EntityType"/> is deliberately free-text per ADR 004
/// (<c>Quotinator.Data</c> cannot know about Engine's entity types) — so its four values are
/// hand-listed here, matching the literals <c>ImportActionPlanner</c> actually writes.
/// </para>
/// <para>
/// <c>genre</c> is deliberately excluded — an open, extensible tag set, not a closed enum.
/// </para>
/// </remarks>
internal sealed class EnumParameterSchemaTransformer : IOpenApiOperationTransformer
{
    private static readonly string[] QuoteTypeValues = [.. InputValidation.ValidTypes];
    private static readonly string[] SearchFieldValues = [.. InputValidation.ValidSearchFields];
    private static readonly string[] ImportActionStatusValues = [.. Enum.GetValues<ImportActionStatus>().Select(v => v.ToString())];
    private static readonly string[] EntityTypeValues = ["Quote", "Source", "Character", "Person"];

    internal static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string[]>> EnumParamsByPath =
        new Dictionary<string, IReadOnlyDictionary<string, string[]>>(StringComparer.OrdinalIgnoreCase)
        {
            ["api/v1/quotes"] = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["type"] = QuoteTypeValues,
            },
            ["api/v1/quotes/random"] = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["type"] = QuoteTypeValues,
            },
            ["api/v1/quotes/search"] = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["type"] = QuoteTypeValues,
                ["field"] = SearchFieldValues,
            },
            ["api/v1/import/actions"] = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["status"] = ImportActionStatusValues,
                ["entityType"] = EntityTypeValues,
            },
        };

    /// <inheritdoc/>
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var path = (context.Description.RelativePath ?? string.Empty).TrimEnd('/');
        if (!EnumParamsByPath.TryGetValue(path, out var paramMap))
            return Task.CompletedTask;

        foreach (var param in operation.Parameters ?? [])
        {
            if (param.Name is null || !paramMap.TryGetValue(param.Name, out var values)
                || param is not OpenApiParameter p || p.Schema is not OpenApiSchema schema)
                continue;

            var targetSchema = schema.Items is OpenApiSchema items ? items : schema;
            targetSchema.Enum = [.. values.Select(v => (JsonNode)v)];
        }

        return Task.CompletedTask;
    }
}

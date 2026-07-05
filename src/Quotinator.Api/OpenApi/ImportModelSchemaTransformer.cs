using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Quotinator.Core.Models;

namespace Quotinator.Api.OpenApi;

/// <summary>
/// Two schema corrections for the import endpoints' response models, mirroring
/// <see cref="YearParameterSchemaTransformer"/>'s "the real type is more specific than what the
/// generator inferred" pattern — applied to response bodies instead of query parameters.
/// </summary>
/// <remarks>
/// <para>
/// <b>Integer properties</b> (<see cref="ImportSummary"/>'s row counts): the OpenAPI generator's
/// default schema for a plain <c>int</c> property is a <c>["integer","string"]</c> union with a
/// numeric-string regex pattern — there is no <c>string</c>-typed workaround behind these fields
/// (unlike the year query parameters), so the accurate schema is simply <c>integer</c>.
/// </para>
/// <para>
/// <b>Enum-shaped string properties</b> (<see cref="ImportResultResponse.ConflictPolicy"/>,
/// <see cref="ImportConflictEntry.AppliedPolicy"/>, <see cref="ImportConflictEntry.Status"/>): these
/// are plain <c>string</c> in C# because <c>Quotinator.Core</c> cannot reference
/// <c>Quotinator.Data</c>'s <c>DuplicateResolutionPolicy</c> enum, but the wire values are a fixed,
/// known set — the schema should list them as an <c>enum</c> rather than an unconstrained string.
/// </para>
/// </remarks>
internal sealed class ImportModelSchemaTransformer : IOpenApiSchemaTransformer
{
    private static readonly string[] PolicyValues = ["skip", "newest-wins", "merge-ours", "merge-theirs", "review"];
    private static readonly string[] StatusValues  = ["resolved", "pending"];

    /// <inheritdoc/>
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (schema.Properties is null)
            return Task.CompletedTask;

        foreach (var property in context.JsonTypeInfo.Properties)
        {
            if (!schema.Properties.TryGetValue(property.Name, out var propertySchemaRef) || propertySchemaRef is not OpenApiSchema propertySchema)
                continue;

            if (property.PropertyType == typeof(int) || property.PropertyType == typeof(int?))
            {
                propertySchema.Type = JsonSchemaType.Integer;
                propertySchema.Pattern = null;
            }
        }

        if (context.JsonTypeInfo.Type == typeof(ImportResultResponse))
            SetEnum(schema, "conflictPolicy", PolicyValues);

        if (context.JsonTypeInfo.Type == typeof(ImportConflictEntry))
        {
            SetEnum(schema, "appliedPolicy", PolicyValues);
            SetEnum(schema, "status", StatusValues);
        }

        return Task.CompletedTask;
    }

    private static void SetEnum(OpenApiSchema schema, string propertyName, IReadOnlyList<string> values)
    {
        if (schema.Properties is null) return;
        if (!schema.Properties.TryGetValue(propertyName, out var propertySchemaRef) || propertySchemaRef is not OpenApiSchema propertySchema)
            return;

        propertySchema.Enum = values.Select(v => (System.Text.Json.Nodes.JsonNode)v).ToList();
    }
}

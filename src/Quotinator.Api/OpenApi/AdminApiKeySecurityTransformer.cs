using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Quotinator.Api.Endpoints.Filters;

namespace Quotinator.Api.OpenApi;

/// <summary>Adds the <c>ApiKey</c> security requirement to every operation that actually requires it.</summary>
/// <remarks>
/// Detection is based on <see cref="AdminApiKeyRequiredMarker"/> endpoint metadata rather than the
/// operation's OpenAPI tag. Tag-based matching only covered the <c>Admin</c> tag, so every
/// <c>Import</c>-tagged endpoint that also requires <c>X-Api-Key</c> (the conflict-review write
/// endpoints, and the file-import endpoints) never showed a security requirement in the spec even
/// though they return <c>401</c> without the key at runtime.
/// </remarks>
internal sealed class AdminApiKeySecurityTransformer : IOpenApiOperationTransformer
{
    /// <inheritdoc/>
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var requiresAdminApiKey = context.Description.ActionDescriptor.EndpointMetadata
            .Any(m => m is AdminApiKeyRequiredMarker);

        if (requiresAdminApiKey)
        {
            operation.Security = new List<OpenApiSecurityRequirement>
            {
                new() { [new OpenApiSecuritySchemeReference("ApiKey")] = new List<string>() }
            };
        }

        return Task.CompletedTask;
    }
}

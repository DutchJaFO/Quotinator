namespace Quotinator.Api.Endpoints.Filters;

/// <summary>
/// Endpoint metadata marker attached alongside <see cref="AdminApiKeyFilter"/> registrations, so
/// <see cref="Quotinator.Api.OpenApi.AdminApiKeySecurityTransformer"/> can detect which operations
/// actually require <c>X-Api-Key</c> without relying on OpenAPI tag names.
/// </summary>
internal sealed class AdminApiKeyRequiredMarker
{
    /// <summary>Shared singleton instance — no per-endpoint state is needed.</summary>
    internal static readonly AdminApiKeyRequiredMarker Instance = new();
}

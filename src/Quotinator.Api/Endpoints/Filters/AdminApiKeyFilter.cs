using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Quotinator.Api.Endpoints.Filters;

/// <summary>
/// Endpoint filter that guards admin endpoints with a static API key.
/// Clients must supply <c>X-Api-Key: &lt;key&gt;</c>.
/// If <c>Quotinator:AdminApiKey</c> is not configured the endpoints return 401 — disabled by default.
/// </summary>
/// <param name="configuration">
/// Supplies <c>Quotinator:AdminApiKey</c>. Injected rather than resolved from the request's services
/// so the filter can be registered once and passed as an instance — activating it by type makes the
/// framework probe for a constructor it does not have, throwing once per registration (#397).
/// </param>
internal sealed class AdminApiKeyFilter(IConfiguration configuration) : IEndpointFilter
{
    /// <summary>Validates the X-Api-Key header before invoking the endpoint.</summary>
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        string? expectedKey = configuration["Quotinator:AdminApiKey"];

        if (string.IsNullOrEmpty(expectedKey))
            return Results.Unauthorized();

        string providedKey = context.HttpContext.Request.Headers["X-Api-Key"].ToString().Trim();

        if (!KeysMatch(expectedKey, providedKey))
            return Results.Unauthorized();

        return await next(context);
    }

    private static bool KeysMatch(string expected, string provided)
    {
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expected);
        byte[] providedBytes = Encoding.UTF8.GetBytes(provided);

        if (expectedBytes.Length != providedBytes.Length)
        {
            // Always run a dummy comparison to prevent length-based timing leaks.
            CryptographicOperations.FixedTimeEquals(expectedBytes, expectedBytes);
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}

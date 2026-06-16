using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Quotinator.Api.Endpoints.Filters;

/// <summary>
/// Endpoint filter that guards admin endpoints with a static API key.
/// Clients must supply <c>Authorization: Bearer &lt;key&gt;</c>.
/// If <c>Quotinator:AdminApiKey</c> is not configured the endpoints return 401 — disabled by default.
/// </summary>
internal sealed class AdminApiKeyFilter : IEndpointFilter
{
    /// <summary>Validates the Authorization header before invoking the endpoint.</summary>
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var expectedKey = config["Quotinator:AdminApiKey"];

        if (string.IsNullOrEmpty(expectedKey))
            return Results.Unauthorized();

        var authHeader = context.HttpContext.Request.Headers.Authorization.ToString();
        var providedKey = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authHeader["Bearer ".Length..].Trim()
            : string.Empty;

        if (!KeysMatch(expectedKey, providedKey))
            return Results.Unauthorized();

        return await next(context);
    }

    private static bool KeysMatch(string expected, string provided)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(provided);

        if (expectedBytes.Length != providedBytes.Length)
        {
            // Always run a dummy comparison to prevent length-based timing leaks.
            CryptographicOperations.FixedTimeEquals(expectedBytes, expectedBytes);
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}

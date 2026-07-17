namespace Quotinator.Constants.Api;

/// <summary>Default values for numeric query parameters, shared across handler fallbacks, <c>[DefaultValue]</c> attributes, and the OpenAPI schema transformer.</summary>
public static class QueryParamDefaults
{
    /// <summary>Default page number for <c>GET /api/v1/quotes</c>.</summary>
    public const int Page = 1;

    /// <summary>Default page size for <c>GET /api/v1/quotes</c>.</summary>
    public const int PageSize = 20;

    /// <summary>Default result limit for <c>GET /api/v1/quotes/search</c>.</summary>
    public const int SearchLimit = 20;

    /// <summary>Default quote count for <c>GET /api/v1/quotes/random</c>.</summary>
    public const int RandomCount = 1;
}

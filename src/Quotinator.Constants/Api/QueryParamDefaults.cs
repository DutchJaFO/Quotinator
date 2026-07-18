namespace Quotinator.Constants.Api;

/// <summary>Default values for numeric query parameters, shared across handler fallbacks, <c>[DefaultValue]</c> attributes, and the OpenAPI schema transformer.</summary>
public static class QueryParamDefaults
{
    /// <summary>Default page number — standard across every paginated endpoint (<c>/quotes</c>, <c>/admin/audit</c>, <c>/import/actions</c>).</summary>
    public const int Page = 1;

    /// <summary>Default page size — standard across every paginated endpoint (<c>/quotes</c>, <c>/admin/audit</c>, <c>/import/actions</c>). No per-endpoint range.</summary>
    public const int PageSize = 20;

    /// <summary>Maximum allowed <c>pageSize</c> — standard across every paginated endpoint. <c>pageSize = 0</c> ("every row as one page") deliberately bypasses this ceiling.</summary>
    public const int PageSizeMax = 500;

    /// <summary>Default result limit for <c>GET /api/v1/quotes/search</c>.</summary>
    public const int SearchLimit = 20;

    /// <summary>Default quote count for <c>GET /api/v1/quotes/random</c>.</summary>
    public const int RandomCount = 1;
}

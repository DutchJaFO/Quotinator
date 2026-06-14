namespace Quotinator.Constants;

/// <summary>Extension methods for route string constants.</summary>
public static class RouteExtensions
{
    /// <summary>Strips the leading slash so the route resolves relative to &lt;base href&gt; in Blazor markup.</summary>
    public static string AsLink(this string route) => route.TrimStart('/');
}

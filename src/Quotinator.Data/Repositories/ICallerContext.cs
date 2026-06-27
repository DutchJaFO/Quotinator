namespace Quotinator.Data.Repositories;

/// <summary>
/// Holds the identity of the caller for the current request.
/// Set by the request logging middleware from the <c>User-Agent</c> header.
/// Read by the repository base class when constructing audit entries.
/// </summary>
/// <remarks>
/// Implemented as a singleton using <c>AsyncLocal&lt;string?&gt;</c> so that each async
/// execution context (i.e. each HTTP request) maintains its own value without lifetime
/// conflicts between singleton repositories and scoped request state.
/// </remarks>
public interface ICallerContext
{
    /// <summary>
    /// The caller's <c>User-Agent</c> header value, or <c>null</c> when the header was absent.
    /// </summary>
    string? Agent { get; set; }
}

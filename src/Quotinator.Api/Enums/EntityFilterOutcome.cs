namespace Quotinator.Api.Enums;

/// <summary>
/// Which of the two mutually-exclusive filter forms
/// <see cref="Quotinator.Api.Endpoints.Shared.EntityFilterParsing.ResolveAsync"/> resolved to.
/// </summary>
internal enum EntityFilterOutcome
{
    /// <summary>Neither the id-valued nor the name-valued parameter was supplied.</summary>
    NoFilter,

    /// <summary>An id was resolved — either supplied directly or found by name.</summary>
    Resolved,

    /// <summary>A name-valued filter was supplied but no matching entity exists — a legitimate zero-results case, not an error.</summary>
    NotFound,

    /// <summary>Both parameters were supplied, or the id-valued one was malformed.</summary>
    Error,
}

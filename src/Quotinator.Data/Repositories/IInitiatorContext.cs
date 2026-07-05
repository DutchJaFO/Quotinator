using Quotinator.Data.Models;

namespace Quotinator.Data.Repositories;

/// <summary>
/// Extends <see cref="ICallerContext"/> with the identity of whatever initiated a
/// <see cref="Entities.SystemChangeLog"/> write — the mechanism (<see cref="InitiatedByType"/>) plus a
/// specific identifying detail (<see cref="InitiatedById"/>: a batch UUID, an HTTP route, a provider
/// name). Kept separate from <see cref="ICallerContext.Agent"/>, which callers unrelated to change
/// logging (e.g. <see cref="SqliteRepository{T}"/>'s own <c>System_AuditEntries</c> writes) already
/// depend on unchanged.
/// </summary>
public interface IInitiatorContext : ICallerContext
{
    /// <summary>The mechanism that initiated the current write, or <c>null</c> when not yet set for this async context.</summary>
    InitiatorType? InitiatedByType { get; set; }

    /// <summary>Specific identifying detail for the initiator, or <c>null</c>.</summary>
    string? InitiatedById { get; set; }
}

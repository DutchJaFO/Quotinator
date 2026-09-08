using Quotinator.Data.Entities;

namespace Quotinator.Data.Enums;

/// <summary>
/// The kinds of action a <see cref="ImportActionEntity"/> row can represent — a closed set defined
/// and maintained entirely by this project's own coordinator logic, not by any consuming project's
/// schema (a consumer decides, per row, which of these kinds applies — it does not invent new
/// kinds of its own). Per ADR 008, backed by a matching SQL CHECK constraint.
/// </summary>
public enum ImportActionKind
{
    /// <summary>A brand-new record with no existing counterpart.</summary>
    Add,

    /// <summary>An existing record whose fields would change.</summary>
    Modify,

    /// <summary>
    /// An existing record the import would leave exactly as it is (#373). Distinct from <see cref="Modify"/>,
    /// which claims a write that never happens, and from producing no action at all, which is how every
    /// non-Quote entity used to disappear from a report — leaving a reader unable to tell content that
    /// arrived and was already correct from content a file never mentioned.
    /// </summary>
    Unchanged,

    /// <summary>
    /// An existing record whose fields <em>did</em> differ from what arrived, but whose resolution
    /// settled on exactly the values already stored, so nothing is written differently (#377).
    /// <para>
    /// Distinct from <see cref="Unchanged"/>, which means the file and the database agreed in the first
    /// place — here they disagreed and the resolution kept the stored side. Distinct from
    /// <see cref="Modify"/>, which claims a write that never happens. Distinct too from a
    /// <c>Skip</c>-policy Modify, where a real difference arrived and was discarded by policy rather
    /// than resolved away; that stays a <see cref="Modify"/> and is counted separately (#374).
    /// </para>
    /// <para>
    /// Terminal, and that is load-bearing: an action of this kind is staged
    /// <c>ImportActionStatus.Applied</c>, and <c>ImportActionResolutionCoordinator.TryApplyBatchAsync</c>
    /// applies only <c>Decided</c> rows — so nothing re-stamps <c>DateModified</c>, nothing
    /// re-attributes <c>ImportBatchId</c>, and nothing writes an <c>Audit_Change</c> row claiming a
    /// modification that did not happen.
    /// </para>
    /// </summary>
    ResolvedToExisting
}

namespace Quotinator.Data.Enums;

/// <summary>
/// How a notification's own action settled it (#308), recorded alongside
/// <see cref="NotificationDismissReason.Resolved"/>.
/// </summary>
/// <remarks>
/// Distinct from <see cref="NotificationDismissReason"/>, which says only *that* a notification stopped
/// being active. Found in T1: a resolved review alert read `Done` while its body still said "1 changes
/// need your decision before they can be applied" — the body is frozen at write time, and the
/// <c>FieldResolutionChoice</c> that settled it was discarded rather than stored, so nothing could say
/// which way it went.
/// <para>
/// Only set when an action carried the notification to completion. A notification the operator simply
/// dismissed, or one superseded by a reseed, has no resolution — those are
/// <see cref="NotificationDismissReason"/>'s business.
/// </para>
/// </remarks>
public enum NotificationResolution
{
    /// <summary>An import review was settled by keeping every stored value (#303's <c>Keep</c>).</summary>
    KeptExisting,

    /// <summary>An import review was settled by taking every incoming value (#303's <c>Replace</c>).</summary>
    TookIncoming,

    /// <summary>The notification's reseed action ran to completion (#304).</summary>
    Reseeded,

    /// <summary>The notification's database-reset action ran to completion (#289).</summary>
    Reset,
}

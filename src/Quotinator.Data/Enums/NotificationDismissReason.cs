namespace Quotinator.Data.Enums;

/// <summary>
/// Why a notification stopped being active (#304) — the user set it aside, or the thing it was about
/// was actually dealt with.
/// <para>
/// Both used to be recorded as nothing more than <c>IsDismissed = 1</c>, so a user who ran a
/// notification's action saw the result reported as *dismissed*, which reads as having declined it.
/// Found in #304's T1 pass, though the behaviour dates from #278: every actionable notification had it.
/// </para>
/// <para>
/// Per ADR 008, backed by a matching (nullable-aware) SQL CHECK constraint. <see langword="null"/> means
/// the notification is still active, or that it was dismissed before this column existed — an unknown
/// reason, which is why there is no <c>Unknown</c> member to confuse with a recorded one.
/// </para>
/// </summary>
public enum NotificationDismissReason
{
    /// <summary>
    /// The user chose to set the notification aside without acting on it — the Dismiss control.
    /// </summary>
    Dismissed,

    /// <summary>
    /// The condition the notification described was resolved: its own action was run, or something else
    /// carried out the same work (a reseed through the admin endpoint, an import that populated content).
    /// The user is not being told they declined something they in fact did.
    /// </summary>
    Resolved
}

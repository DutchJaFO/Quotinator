using Quotinator.Data.Entities;

namespace Quotinator.Data.Enums;

/// <summary>
/// Identifies which action, if performed, supersedes a <see cref="NotificationEntity"/> — e.g. an
/// <see cref="NotificationType.ActionRequired"/> notification recommending a database Reset carries
/// <see cref="DatabaseReset"/>, so <c>POST /admin/database/reset</c> can dismiss it automatically once
/// that action actually completes (#278). Per ADR 008, backed by a matching (nullable-aware) SQL CHECK
/// constraint. New members are added here — plus their own migration extending the CHECK — only as
/// concrete producer integrations need them, not speculatively.
/// </summary>
public enum NotificationDismissTrigger
{
    /// <summary>Superseded by a successful <c>POST /admin/database/reset</c>.</summary>
    DatabaseReset,

    /// <summary>
    /// Superseded by content arriving: a successful reseed (via the notification action or
    /// <c>POST /admin/database/reseed</c>), or a successful import that populates content (#304).
    /// <para>
    /// Unlike <see cref="DatabaseReset"/>, this marks a <b>recurring condition</b> rather than a
    /// one-off event, so its producer dedupes against active rows only
    /// (<c>NotificationSeeding.SeedWhileUnresolvedAsync</c>). Dismissal is therefore load-bearing: every
    /// path that resolves the condition must dismiss this trigger, or the notification stays active and
    /// silently suppresses every later occurrence.
    /// </para>
    /// </summary>
    Reseed,

    /// <summary>
    /// Superseded by the staged import actions it reported being resolved — decided and applied, or
    /// discarded (#303).
    /// <para>
    /// Unlike <see cref="Reseed"/>, this trigger classifies *what kind* of resolution supersedes the
    /// notification but cannot on its own select which one to dismiss: several files can each leave a
    /// batch awaiting review, so resolving one must not clear the others. The selector is the batch id
    /// carried in the notification's own payload.
    /// </para>
    /// </summary>
    ImportReviewResolved
}

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
    DatabaseReset
}

using Quotinator.Data.Enums;

namespace Quotinator.Data.Notifications;

/// <summary>What one registered condition check did in a run of <see cref="NotificationConditionChecks"/> (#348).</summary>
/// <param name="Kind">The kind of notification the check covers.</param>
/// <param name="Outcome">What it did.</param>
public sealed record NotificationConditionCheckResult(NotificationMetadataKind Kind, NotificationConditionOutcome Outcome);

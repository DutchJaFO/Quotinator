namespace Quotinator.Core.Models;

/// <summary>
/// What <c>POST /api/v1/notifications/refresh</c> did (#348): one entry per registered condition check, in
/// the order they ran.
/// </summary>
public sealed record NotificationRefreshResponse
{
    /// <summary>What each condition check did.</summary>
    public required IReadOnlyList<NotificationConditionCheckDto> Checks { get; init; }
}

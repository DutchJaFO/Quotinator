namespace Quotinator.Core.Models;

/// <summary>What one condition check did during a notification refresh (#348).</summary>
public sealed record NotificationConditionCheckDto
{
    /// <summary>The kind of notification the check covers, lowercase, as <c>metadataKind</c> reads elsewhere.</summary>
    public required string Kind { get; init; }

    /// <summary>What it did, lowercase: <c>raised</c>, <c>cleared</c> or <c>unchanged</c>.</summary>
    public required string Outcome { get; init; }
}

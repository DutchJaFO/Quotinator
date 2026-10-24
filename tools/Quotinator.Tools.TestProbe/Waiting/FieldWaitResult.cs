namespace Quotinator.Tools.TestProbe.Waiting;

/// <summary>The outcome of waiting for a field to hold one of a set of values.</summary>
/// <param name="Reached">True when the field held one of the values before the timeout.</param>
/// <param name="Body">The last body read, or null when none was ever read.</param>
/// <param name="LastValue">The field's last value read, or null when it was never present.</param>
internal sealed record FieldWaitResult(bool Reached, string? Body, string? LastValue);

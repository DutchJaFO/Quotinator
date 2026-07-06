namespace Quotinator.Data.Import;

/// <summary>
/// A caller-supplied decision for one field during manual conflict resolution (#149). Always wins over
/// auto-resolution, regardless of whether the field was actually ambiguous — the caller may override
/// any field, not only ones that needed a tiebreaker. <see cref="CustomValue"/> is only meaningful when
/// <see cref="Choice"/> is <see cref="FieldResolutionChoice.Custom"/>.
/// </summary>
public readonly record struct FieldMergeDecision(FieldResolutionChoice Choice, object? CustomValue);

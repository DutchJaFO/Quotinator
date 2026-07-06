using Quotinator.Data.Import;

namespace Quotinator.Engine.Models;

/// <summary>A caller's decision for the <c>genres</c> field during manual conflict resolution (#149) — a list, unlike every other mergeable field.</summary>
public sealed class GenresFieldDecision
{
    /// <summary>Which side wins for this field, or whether a custom list overrides both.</summary>
    public required FieldResolutionChoice Choice { get; init; }

    /// <summary>
    /// The custom genre list, when <see cref="Choice"/> is <see cref="FieldResolutionChoice.Custom"/>.
    /// Ignored otherwise. Can be a union, a subset, or unrelated to either side — full manual control,
    /// same as any other field.
    /// </summary>
    public List<string>? Value { get; init; }
}

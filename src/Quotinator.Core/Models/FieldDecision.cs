using Quotinator.Data.Import;

namespace Quotinator.Core.Models;

/// <summary>A caller's decision for one scalar field during manual conflict resolution (#149).</summary>
public sealed class FieldDecision
{
    /// <summary>Which side wins for this field, or whether a custom value overrides both.</summary>
    public required FieldResolutionChoice Choice { get; init; }

    /// <summary>The custom value, when <see cref="Choice"/> is <see cref="FieldResolutionChoice.Custom"/>. Ignored otherwise.</summary>
    public string? Value { get; init; }
}

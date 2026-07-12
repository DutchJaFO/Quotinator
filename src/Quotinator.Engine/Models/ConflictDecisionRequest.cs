using Quotinator.Data.Entities;

namespace Quotinator.Engine.Models;

/// <summary>
/// Request body for <c>POST /api/v1/import/conflicts/{id}/decide</c> (#149). One property per
/// mergeable field (mirrors <c>QuoteFieldMerge</c>'s field set exactly) — a field left <c>null</c> has
/// no decision supplied and auto-resolves (empty-side wins, equal values keep existing); a field that
/// is genuinely ambiguous (both sides non-empty and differ) with no decision causes a 422.
/// </summary>
public sealed class ConflictDecisionRequest
{
    /// <summary>Decision for the quote's text.</summary>
    public FieldDecision? QuoteText { get; init; }

    /// <summary>Decision for the original-language code.</summary>
    public FieldDecision? OriginalLanguage { get; init; }

    /// <summary>Decision for the source (film/book/show title).</summary>
    public FieldDecision? Source { get; init; }

    /// <summary>Decision for the date.</summary>
    public FieldDecision? Date { get; init; }

    /// <summary>Decision for the character.</summary>
    public FieldDecision? Character { get; init; }

    /// <summary>Decision for the author.</summary>
    public FieldDecision? Author { get; init; }

    /// <summary>Decision for the quote type.</summary>
    public FieldDecision? Type { get; init; }

    /// <summary>Decision for the genre list.</summary>
    public GenresFieldDecision? Genres { get; init; }

    /// <summary>Decision for a Source action's title (#162).</summary>
    public FieldDecision? SourceTitle { get; init; }

    /// <summary>Decision for a Source action's type (#162).</summary>
    public FieldDecision? SourceType { get; init; }

    /// <summary>Decision for a Source action's date (#162).</summary>
    public FieldDecision? SourceDate { get; init; }

    /// <summary>
    /// Optional, entity-agnostic override (#165) — when supplied, applying this decision sets the
    /// target record's <see cref="CompletenessStatus"/> directly (most usefully <c>Complete</c>),
    /// regardless of its current value. Available on every decide call, for any entity type, not
    /// only when resolving a <c>Blocked</c> action. When omitted, the target's completeness status
    /// is instead computed automatically at apply time.
    /// </summary>
    public CompletenessStatus? MarkCompletenessAs { get; init; }
}

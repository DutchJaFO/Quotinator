using Quotinator.Data.Enums;
using Quotinator.Data.Import;

namespace Quotinator.Core.Models;

/// <summary>
/// Response envelope for the <c>/api/v1/import/rules/conflict</c> endpoints (#153) — the currently
/// effective <see cref="ConflictResolutionRuleFile"/> for a source's <c>ruleFile</c>, plus metadata
/// about where that content came from.
/// </summary>
public sealed class ConflictRuleFileResponse
{
    /// <summary>The bundled/image filename this rule file corresponds to (e.g. <c>nikhilnamal17-conflict-rules.json</c>).</summary>
    public required string FileName { get; init; }

    /// <summary>Wire value of the <see cref="SeedBatchOrigin"/> this file belongs to.</summary>
    public required string Origin { get; init; }

    /// <summary><see langword="true"/> when a registered, hash-verified override is currently in effect instead of the bundled copy.</summary>
    public required bool IsOverrideActive { get; init; }

    /// <summary>Every rule currently in effect.</summary>
    public required IReadOnlyList<ConflictResolutionRule> Rules { get; init; }

    /// <summary>Rules newly added by a generate call. Always <c>0</c> on a plain read.</summary>
    public int RulesAdded { get; init; }
}

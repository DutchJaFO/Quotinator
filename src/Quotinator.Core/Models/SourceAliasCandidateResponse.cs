using Quotinator.Data.Enums;
using Quotinator.Data.Import;

namespace Quotinator.Core.Models;

/// <summary>
/// Response envelope for <c>GET /api/v1/import/rules/alias</c> (#153 Step 13) — near-duplicate Source
/// pairs found in the live database, not yet covered by <see cref="FileName"/>/<see cref="Origin"/>'s
/// own <see cref="SourceAliasRuleFileDto"/>. Read-only: surfacing a candidate never writes an alias entry —
/// confirming one requires research per <c>docs/workflow/source-verification.md</c> and a hand-edit of
/// the alias file (or a generated override via the same mechanism #153's `ConflictResolutionRule`
/// endpoints use, once a human has verified the canonical pair).
/// </summary>
public sealed class SourceAliasCandidateResponse
{
    /// <summary>The bundled/image filename this alias file corresponds to (e.g. <c>nikhilnamal17-source-aliases.json</c>).</summary>
    public required string FileName { get; init; }

    /// <summary>Wire value of the <see cref="SeedBatchOrigin"/> this file belongs to.</summary>
    public required string Origin { get; init; }

    /// <summary>Every near-duplicate pair found, not already covered by an existing alias entry.</summary>
    public required IReadOnlyList<SourceAliasCandidate> Candidates { get; init; }
}

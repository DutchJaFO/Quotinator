using Dapper.Contrib.Extensions;
using Quotinator.Data.Enums;
using Quotinator.Data.Import;
using Quotinator.Data.Models;

namespace Quotinator.Data.Entities;

/// <summary>
/// Registers a generated override of a bundled or user-imported source's <c>ruleFile</c>/
/// <c>sourceAliasFile</c> (#153) — one row per (<see cref="FileName"/>, <see cref="Origin"/>) pair,
/// upserted whenever the generate-rules endpoint writes a new version. Exists so the seeding pipeline
/// can know, for certain, whether an override is genuinely the one this project's own generation
/// mechanism produced (<see cref="ContentHash"/> matches what's actually on disk) rather than
/// inferring it from file existence alone. Named under the <c>Import_</c> domain per ADR 015/#253.
/// </summary>
/// <remarks>
/// <b>Deliberately not folded into <see cref="FileResourceEntity"/> (#252, confirmed with the
/// developer 2026-08-04)</b>, despite the shape looking similar on paper. Four reasons:
/// <list type="number">
/// <item><description><see cref="FileResourceEntity"/> is a general-purpose text-file-content store —
/// not conceptually tied to imports at all, and reusable by future unrelated consumers. This entity
/// <i>is</i> #153's override-trust mechanism itself; folding a domain-specific trust registry into a
/// generic storage primitive would couple that primitive's future consumers to seeding-specific
/// semantics they have no reason to know about.</description></item>
/// <item><description>This is a current-state registry (<see cref="Repositories.ISourceFileOverrideRegistry"/>
/// is upsert-keyed by <c>(FileName, Origin)</c>, with an explicit <c>RemoveAsync</c>) —
/// <see cref="FileResourceEntity"/> is append-only and content-addressed, with no "unregister this
/// one" operation and no "which row is active" concept.</description></item>
/// <item><description>Reusing <see cref="FileResourceEntity"/> for the trust check would weaken it:
/// <see cref="Repositories.ISourceFileOverrideRegistry.RegisterAsync"/>-equivalent writes happen from
/// exactly one admin-key-gated place, while
/// <see cref="FileResourceEntity"/> is written from pipelines that capture untrusted content too (an
/// uploaded import, a flat scan of the user-imports folder) — a coincidentally-matching content hash
/// from an unrelated import could then vouch for a tampered override file.</description></item>
/// <item><description><see cref="SourceBatchId"/> means "this override's content was generated
/// <i>from</i> batch X" (output provenance) — the opposite direction from
/// <see cref="Entities.FileResourceBatchEntity"/>'s "this file was read <i>as input by</i> batch X"
/// (input consumption).</description></item>
/// </list>
/// See #252's plan doc for the full comparison against #251's actual shipped schema.
/// </remarks>
[Table("Import_SourceFileOverride")]
public sealed class SourceFileOverrideEntity : RecordBase
{
    /// <summary>Plain filename (no path segments) of the overridden <c>ruleFile</c>/<c>sourceAliasFile</c>, matching the manifest entry's own value.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Which directory this override lives under — the bundled sources folder or the user imports folder.</summary>
    public SafeValue<SeedBatchOrigin?> Origin { get; init; } = SafeValue<SeedBatchOrigin?>.Empty;

    /// <summary>SHA-256 hash (lowercase hex) of the override file's current content, checked against the file on disk before it's trusted.</summary>
    public string ContentHash { get; init; } = string.Empty;

    /// <summary>Loose reference to the batch whose decided actions produced this override, when generated from one. No FK — this project doesn't know the consumer's batch table name.</summary>
    public string? SourceBatchId { get; init; }
}

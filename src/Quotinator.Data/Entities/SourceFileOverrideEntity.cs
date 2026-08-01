using Dapper.Contrib.Extensions;
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

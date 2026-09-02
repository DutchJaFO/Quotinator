namespace Quotinator.Data.Import;

/// <summary>
/// Per-entity-type breakdown of how many <see cref="Entities.ImportActionEntity"/> rows for one
/// imported/seeded file fell into each of the 6 possible outcomes (#221) — replaces the flat
/// "duplicates" count that gave no indication of which file it came from or what actually happened.
/// Every action from a single planning pass falls into exactly one bucket:
/// <list type="bullet">
/// <item><description><see cref="New"/> — an Add action that resolved cleanly (<c>Decided</c> or <c>Applied</c>)</description></item>
/// <item><description><see cref="Modified"/> — a Modify action that resolved cleanly (<c>Decided</c> or <c>Applied</c>)</description></item>
/// <item><description><see cref="Blocked"/> — held because it would change a field on a row confirmed fully reviewed</description></item>
/// <item><description><see cref="Discarded"/> — the owning batch was discarded; never written anywhere</description></item>
/// <item><description><see cref="Pending"/> — genuinely ambiguous, awaiting a manual decision</description></item>
/// <item><description><see cref="Stale"/> — a matching rule existed but its own recorded snapshot no longer matches current data (#153)</description></item>
/// </list>
/// </summary>
public sealed class EntityTypeActionCounts
{
    /// <summary>
    /// Every action that arrived for this entity type (#373) — the sum of every bucket below.
    /// <para>
    /// Reported rather than derived by the reader, so a file's own story reads whole: "5 quotes
    /// incoming, 2 new, 3 already stored". It is also the only way a row that falls through every
    /// outcome arm becomes observable — the total stops adding up.
    /// </para>
    /// </summary>
    public required int Incoming { get; init; }

    /// <summary>Add actions that resolved cleanly.</summary>
    public required int New { get; init; }

    /// <summary>
    /// Actions for a record the import would leave exactly as it is (#373). Distinct from a zero
    /// <see cref="New"/>, which says only that nothing was added — not whether anything arrived.
    /// </summary>
    public required int Unchanged { get; init; }

    /// <summary>Modify actions that resolved cleanly.</summary>
    public required int Modified { get; init; }

    /// <summary>Actions held because they would change a field on a row confirmed fully reviewed.</summary>
    public required int Blocked { get; init; }

    /// <summary>Actions whose owning batch was discarded.</summary>
    public required int Discarded { get; init; }

    /// <summary>Actions genuinely ambiguous, awaiting a manual decision.</summary>
    public required int Pending { get; init; }

    /// <summary>Actions whose matching rule's own recorded snapshot no longer matches current data.</summary>
    public required int Stale { get; init; }
}

/// <summary>One imported/seeded file's report — a set of <see cref="EntityTypeActionCounts"/> keyed by entity type (e.g. <c>"Quote"</c>, <c>"Source"</c>). An entity type with zero actions for this file is omitted.</summary>
public sealed class FileImportReport
{
    /// <summary>File name without directory path.</summary>
    public required string FileName { get; init; }

    /// <summary>Per-entity-type action counts, keyed by the caller's own entity-type constant (e.g. Quotinator.Core.Helpers.ImportActionEntityTypes). Entity types with no actions for this file are omitted.</summary>
    public required IReadOnlyDictionary<string, EntityTypeActionCounts> EntityTypes { get; init; }
}

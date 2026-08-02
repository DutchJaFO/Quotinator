using Dapper.Contrib.Extensions;
using Quotinator.Data.Models;

namespace Quotinator.Data.Entities;

/// <summary>
/// Links one <see cref="FileResourceEntity"/> to one import batch it produced (#251) — a many-to-many
/// association: a single file resource can be the source of many batches over time (re-seeding an
/// unchanged file), and a single import call can span multiple source files. Full <see cref="RecordBase"/>
/// shape per ADR 002; the natural key (<see cref="FileResourceId"/>, <see cref="ImportBatchId"/>) is
/// enforced as a <c>UNIQUE</c> constraint rather than the primary key, matching
/// <c>Quotinator_CharacterSource</c>/<c>Quotinator_QuoteGenre</c>'s own shape.
/// </summary>
[Table("Import_FileResourceBatch")]
public sealed class FileResourceBatchEntity : RecordBase
{
    /// <summary>The file resource that produced the batch.</summary>
    public Guid FileResourceId { get; init; }

    /// <summary>The import batch this file resource produced. References <c>Import_Batch</c> (Quotinator.Core-owned) — no FK type here, matching this project's Data/Core boundary.</summary>
    public Guid ImportBatchId { get; init; }

    /// <summary>UTC timestamp when this link was recorded (the import/seed event's own timestamp).</summary>
    public SafeValue<DateTime?> ImportedAt { get; init; } = SafeValue<DateTime?>.Empty;
}

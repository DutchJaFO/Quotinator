using Dapper.Contrib.Extensions;
using Quotinator.Data.Models;

namespace Quotinator.Data.Entities;

/// <summary>
/// One literal line of a <see cref="FileResourceEntity"/>'s raw content (#251), in order by
/// <see cref="LineNumber"/> — the line's own terminator is stripped and reconstructed from the parent
/// row's <see cref="FileResourceEntity.LineEnding"/> at read time, not stored per line. Full
/// <see cref="RecordBase"/> shape per ADR 002 (a child/junction-style row is explicitly not exempt);
/// the natural key (<see cref="FileResourceId"/>, <see cref="LineNumber"/>) is enforced as a
/// <c>UNIQUE</c> constraint rather than the primary key, matching
/// <c>Quotinator_CharacterSource</c>/<c>Quotinator_QuoteGenre</c>'s own shape.
/// </summary>
[Table("Import_FileResourceLine")]
public sealed class FileResourceLineEntity : RecordBase
{
    /// <summary>The file resource this line belongs to.</summary>
    public Guid FileResourceId { get; init; }

    /// <summary>1-based position of this line within the file's own content.</summary>
    public int LineNumber { get; init; }

    /// <summary>The line's own text, with its line terminator stripped.</summary>
    public string Text { get; init; } = string.Empty;
}

using Dapper.Contrib.Extensions;
using Quotinator.Data.Enums;
using Quotinator.Data.Models;

namespace Quotinator.Data.Entities;

/// <summary>
/// Records the actual content of one distinct version of an import/seed source file (#251) —
/// deduplicated by <see cref="ContentHash"/>, so re-capturing unchanged content updates
/// <see cref="LastSeenAtUtc"/> instead of inserting a new row. The file's own text lives in
/// <see cref="Entities.FileResourceLineEntity"/> rows, one per literal line; this row carries only the
/// metadata needed to reconstruct it, plus enough to identify where the content came from.
/// </summary>
[Table("Import_FileResource")]
public sealed class FileResourceEntity : RecordBase
{
    /// <summary>Plain filename (no path segments).</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// Where the file lived within its own source root — relative to <c>data/sources/</c> for
    /// <see cref="Enums.FileResourceOrigin.Bundled"/>, relative to <c>{dataDir}/imports/</c> for
    /// <see cref="Enums.FileResourceOrigin.UserImports"/>. Deliberately never the expanded,
    /// <c>{dataDir}</c>-inclusive absolute path, so a later change to the deployment's configured data
    /// directory never invalidates a historical row. Always <c>null</c> for
    /// <see cref="Enums.FileResourceOrigin.Uploaded"/> — a multipart upload carries no folder
    /// information.
    /// </summary>
    public string? OriginalFolderPath { get; init; }

    /// <summary>Which of the three file sources this project accepts content from.</summary>
    public SafeValue<FileResourceOrigin?> Origin { get; init; } = SafeValue<FileResourceOrigin?>.Empty;

    /// <summary>SHA-256 hash (lowercase hex) of the file's raw content. Unique — enforces the dedup-by-content invariant.</summary>
    public string ContentHash { get; init; } = string.Empty;

    /// <summary>
    /// The line-terminator style detected in the file's own content. Recorded once per file — this
    /// project's own confirmed assumption is that line endings are uniform within a single file.
    /// </summary>
    public SafeValue<LineEndingStyle?> LineEnding { get; init; } = SafeValue<LineEndingStyle?>.Empty;

    /// <summary>Whether the file's own content ended with a trailing line terminator.</summary>
    public bool EndsWithTrailingNewline { get; init; }

    /// <summary>
    /// Name of the <c>IQuoteSourceConverter</c> plugin used to interpret this content, or
    /// <see langword="null"/> when the content was already in Quotinator's canonical schema and needed
    /// no conversion. On a content-hash dedup hit, overwritten with the latest capture's value
    /// alongside <see cref="LastSeenAtUtc"/> rather than frozen at first capture.
    /// </summary>
    public string? Converter { get; init; }

    /// <summary>
    /// The converter options passed to <see cref="Converter"/>, as raw JSON text — opaque and
    /// undeserialized, matching <c>SourceImportSettingsDto.ConverterOptions</c>'s own treatment.
    /// <see langword="null"/> when <see cref="Converter"/> is <see langword="null"/>. Overwritten on a
    /// dedup hit the same way as <see cref="Converter"/>.
    /// </summary>
    public string? ConverterOptions { get; init; }

    /// <summary>UTC timestamp when this exact content was first captured.</summary>
    public SafeValue<DateTime?> FirstSeenAtUtc { get; init; } = SafeValue<DateTime?>.Empty;

    /// <summary>UTC timestamp when this exact content was most recently captured again (re-seed/re-import of an unchanged file).</summary>
    public SafeValue<DateTime?> LastSeenAtUtc { get; init; } = SafeValue<DateTime?>.Empty;
}

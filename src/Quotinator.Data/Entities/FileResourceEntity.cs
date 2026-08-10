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
    /// Where the file lived, relative to whichever root <see cref="HomeDirectoryKey"/> names.
    /// Deliberately never the expanded, <c>{dataDir}</c>-inclusive absolute path, so a later change to
    /// the deployment's configured data directory never invalidates a historical row. Always
    /// <see langword="null"/> for <see cref="Enums.FileResourceOrigin.Upload"/> — a REST upload carries
    /// no folder information.
    /// </summary>
    public string? OriginalFolderPath { get; init; }

    /// <summary>Which write-path mechanism captured this content — see <see cref="Enums.FileResourceOrigin"/>'s own remarks for why this is deliberately not import-specific.</summary>
    public SafeValue<FileResourceOrigin?> Origin { get; init; } = SafeValue<FileResourceOrigin?>.Empty;

    /// <summary>
    /// Short symbolic key identifying which named root <see cref="OriginalFolderPath"/> is relative to
    /// (e.g. <c>"sources"</c> for the bundled sources folder, <c>"imports"</c> for the user-imports
    /// folder) — deliberately decoupled from <see cref="Origin"/> itself (#252), so a future
    /// <see cref="Enums.FileResourceOrigin.System"/>/<see cref="Enums.FileResourceOrigin.User"/>
    /// consumer unrelated to quote sources can register its own key without stretching what
    /// <see cref="Origin"/> means. Resolving a key to an actual filesystem path is external to this
    /// table (config/a resolver) — never hardcoded per-<see cref="Origin"/>-value. Always
    /// <see langword="null"/> for <see cref="Enums.FileResourceOrigin.Upload"/>, matching
    /// <see cref="OriginalFolderPath"/>'s own null-ness there.
    /// </summary>
    public string? HomeDirectoryKey { get; init; }

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

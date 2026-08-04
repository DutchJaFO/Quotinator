using Quotinator.Data.Enums;

namespace Quotinator.Data.Models;

/// <summary>
/// One row of the paginated file-resource listing (#251) — <see cref="Entities.FileResourceEntity"/>'s
/// display fields plus <see cref="LinkedBatchCount"/>, computed in the same query to avoid an N+1 per
/// row. Deliberately not <see cref="Entities.FileResourceEntity"/> itself with an extra property bolted
/// on — that type is Dapper.Contrib-mapped to the real table shape via <c>[Table]</c>, and this read
/// model's extra computed column would corrupt that mapping for writes.
/// </summary>
public sealed class FileResourceListItem
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; init; }

    /// <summary>Plain filename (no path segments).</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Where the file lived, relative to <see cref="HomeDirectoryKey"/>. See <see cref="Entities.FileResourceEntity.OriginalFolderPath"/>.</summary>
    public string? OriginalFolderPath { get; init; }

    /// <summary>Which write-path mechanism captured this content. See <see cref="Entities.FileResourceEntity.Origin"/>.</summary>
    public SafeValue<FileResourceOrigin?> Origin { get; init; } = SafeValue<FileResourceOrigin?>.Empty;

    /// <summary>Symbolic key identifying which named root <see cref="OriginalFolderPath"/> is relative to. See <see cref="Entities.FileResourceEntity.HomeDirectoryKey"/>.</summary>
    public string? HomeDirectoryKey { get; init; }

    /// <summary>SHA-256 hash (lowercase hex) of the file's raw content.</summary>
    public string ContentHash { get; init; } = string.Empty;

    /// <summary>The line-terminator style detected in the file's own content.</summary>
    public SafeValue<LineEndingStyle?> LineEnding { get; init; } = SafeValue<LineEndingStyle?>.Empty;

    /// <summary>Whether the file's own content ended with a trailing line terminator.</summary>
    public bool EndsWithTrailingNewline { get; init; }

    /// <summary>Name of the converter plugin used to interpret this content, or <see langword="null"/>.</summary>
    public string? Converter { get; init; }

    /// <summary>The converter options as raw JSON text, or <see langword="null"/>.</summary>
    public string? ConverterOptions { get; init; }

    /// <summary>UTC timestamp when this exact content was first captured.</summary>
    public SafeValue<DateTime?> FirstSeenAtUtc { get; init; } = SafeValue<DateTime?>.Empty;

    /// <summary>UTC timestamp when this exact content was most recently captured again.</summary>
    public SafeValue<DateTime?> LastSeenAtUtc { get; init; } = SafeValue<DateTime?>.Empty;

    /// <summary>Number of <c>Import_Batch</c> rows this file resource is linked to.</summary>
    public int LinkedBatchCount { get; init; }
}

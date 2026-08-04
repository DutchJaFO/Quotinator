namespace Quotinator.Core.Models;

/// <summary>
/// Response shape for <c>GET /api/v1/import/file-resources</c> (list) and
/// <c>GET /api/v1/import/file-resources/{id}</c> (detail) — #251. No line content in either shape —
/// that stays on the dedicated <c>GET .../{id}/download</c> endpoint.
/// </summary>
public sealed class FileResourceResponse
{
    /// <summary>Canonical (lowercase) id.</summary>
    public required string Id { get; init; }

    /// <summary>Plain filename (no path segments).</summary>
    public required string FileName { get; init; }

    /// <summary>Where the file lived, relative to <see cref="HomeDirectoryKey"/>. <see langword="null"/> for uploads.</summary>
    public string? OriginalFolderPath { get; init; }

    /// <summary>Which write-path mechanism captured this content: <c>system</c>, <c>user</c>, or <c>upload</c>.</summary>
    public required string Origin { get; init; }

    /// <summary>Symbolic key identifying which named root <see cref="OriginalFolderPath"/> is relative to (e.g. <c>sources</c>, <c>imports</c>). <see langword="null"/> for uploads.</summary>
    public string? HomeDirectoryKey { get; init; }

    /// <summary>SHA-256 hash (lowercase hex) of the file's raw content.</summary>
    public required string ContentHash { get; init; }

    /// <summary>The line-terminator style recorded for this content: <c>lf</c>, <c>crlf</c>, or <c>cr</c>.</summary>
    public required string LineEnding { get; init; }

    /// <summary>Whether the file's own content ended with a trailing line terminator.</summary>
    public bool EndsWithTrailingNewline { get; init; }

    /// <summary>Name of the converter plugin used to interpret this content, or <see langword="null"/>.</summary>
    public string? Converter { get; init; }

    /// <summary>The converter options as raw JSON text, or <see langword="null"/>.</summary>
    public string? ConverterOptions { get; init; }

    /// <summary>UTC timestamp when this exact content was first captured.</summary>
    public DateTime? FirstSeenAtUtc { get; init; }

    /// <summary>UTC timestamp when this exact content was most recently captured again.</summary>
    public DateTime? LastSeenAtUtc { get; init; }

    /// <summary>Number of <c>Import_Batch</c> rows this file resource is linked to. Populated on both list rows and the detail response.</summary>
    public int LinkedBatchCount { get; init; }

    /// <summary>
    /// Ids of every <c>Import_Batch</c> this file resource is linked to, most recent first. Only
    /// populated by the single-item detail endpoint — always <see langword="null"/> on list rows, to
    /// keep the list response bounded regardless of how many batches a long-lived, frequently-reseeded
    /// file has accumulated links to.
    /// </summary>
    public IReadOnlyList<string>? LinkedBatchIds { get; init; }
}

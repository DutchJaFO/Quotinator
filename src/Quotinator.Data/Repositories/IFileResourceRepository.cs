using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Models;

namespace Quotinator.Data.Repositories;

/// <summary>Captures and reads back the actual content of import/seed source files (#251).</summary>
public interface IFileResourceRepository
{
    /// <summary>
    /// Captures <paramref name="content"/>'s current version and links it to <paramref name="importBatchId"/>.
    /// Deduplicated by content hash — if this exact content has already been captured, no new
    /// <see cref="FileResourceEntity"/> row is inserted; instead <c>LastSeenAtUtc</c> is touched and
    /// <paramref name="converter"/>/<paramref name="converterOptions"/> are overwritten with these
    /// latest values (the row always reflects how the content was most recently interpreted).
    /// <paramref name="converter"/> is the name of the <c>IQuoteSourceConverter</c> plugin used to
    /// interpret this content, or <see langword="null"/> when none was needed; <paramref name="converterOptions"/>
    /// is its options as raw JSON text, always <see langword="null"/> when <paramref name="converter"/>
    /// is. Always inserts a new <see cref="Entities.FileResourceBatchEntity"/> link row, since a re-seen
    /// file can legitimately be linked to many batches over time.
    /// </summary>
    /// <returns>The id of the (possibly pre-existing) <see cref="FileResourceEntity"/> row.</returns>
    Task<Guid> WriteAsync(
        string fileName, string? originalFolderPath, FileResourceOrigin origin, string content,
        Guid importBatchId, string? converter = null, string? converterOptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>The file resource row itself, or <c>null</c> if it doesn't exist or is soft-deleted.</summary>
    Task<FileResourceEntity?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every line of a file resource's content, in order — the raw material for reconstructing it.</summary>
    Task<IReadOnlyList<FileResourceLineEntity>> GetLinesAsync(Guid fileResourceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginated file-resource listing, newest-seen first per <c>FileName</c>, with each row's
    /// <see cref="FileResourceListItem.LinkedBatchCount"/>. <paramref name="fileName"/> is an
    /// exact, case-insensitive match; <paramref name="origin"/> filters to one <see cref="FileResourceOrigin"/>.
    /// </summary>
    Task<PagedItems<FileResourceListItem>> GetPageAsync(
        string? fileName, FileResourceOrigin? origin, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Ids of every <c>Import_Batch</c> a file resource is linked to, most recent first — the detail endpoint's <c>linkedBatchIds</c>.</summary>
    Task<IReadOnlyList<Guid>> GetBatchIdsAsync(Guid fileResourceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hard-deletes every <see cref="FileResourceEntity"/> row beyond the <paramref name="keepPerFile"/>
    /// most-recently-seen distinct rows per <c>FileName</c>, cascading to their
    /// <see cref="Entities.FileResourceLineEntity"/>/<see cref="Entities.FileResourceBatchEntity"/> rows.
    /// A batch's own <c>Import_Batch</c> row is never touched — only the file content copy is pruned.
    /// </summary>
    /// <returns>The number of <see cref="FileResourceEntity"/> rows pruned.</returns>
    Task<int> PruneAsync(int keepPerFile, CancellationToken cancellationToken = default);
}

using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Testing.NoOps;

/// <summary>No-op <see cref="IFileResourceRepository"/> for use in tests that do not exercise import-file provenance capture (#251) — nothing is ever written, found, or pruned.</summary>
public sealed class NoOpFileResourceRepository : IFileResourceRepository
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly NoOpFileResourceRepository Instance = new();

    /// <inheritdoc/>
    public Task<Guid> WriteAsync(
        string fileName, string? originalFolderPath, FileResourceOrigin origin, string content,
        Guid importBatchId, string? converter = null, string? converterOptions = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Guid.Empty);

    /// <inheritdoc/>
    public Task<FileResourceEntity?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult<FileResourceEntity?>(null);

    /// <inheritdoc/>
    public Task<IReadOnlyList<FileResourceLineEntity>> GetLinesAsync(Guid fileResourceId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<FileResourceLineEntity>>([]);

    /// <inheritdoc/>
    public Task<PagedItems<FileResourceListItem>> GetPageAsync(
        string? fileName, FileResourceOrigin? origin, int page, int pageSize, CancellationToken cancellationToken = default)
        => Task.FromResult(new PagedItems<FileResourceListItem>([], page, pageSize, 0));

    /// <inheritdoc/>
    public Task<IReadOnlyList<Guid>> GetBatchIdsAsync(Guid fileResourceId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Guid>>([]);

    /// <inheritdoc/>
    public Task<int> PruneAsync(int keepPerFile, CancellationToken cancellationToken = default)
        => Task.FromResult(0);
}

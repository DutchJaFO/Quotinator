using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Helpers;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>In-memory test double for <see cref="IFileResourceRepository"/> — avoids requiring a real database in endpoint tests.</summary>
internal sealed class FakeFileResourceRepository : IFileResourceRepository
{
    private readonly Dictionary<Guid, FileResourceEntity> _resources = [];
    private readonly Dictionary<Guid, List<FileResourceLineEntity>> _lines = [];
    private readonly Dictionary<Guid, List<Guid>> _batchLinks = [];

    public int? LastPruneKeepPerFile { get; private set; }
    public int PruneResult { get; set; }

    /// <summary>Registers a fixed file resource + its lines for a test to look up by id.</summary>
    public void Seed(FileResourceEntity resource, IReadOnlyList<string> lines, IReadOnlyList<Guid>? linkedBatchIds = null)
    {
        _resources[resource.Id] = resource;
        _lines[resource.Id] = [.. lines.Select((text, index) => new FileResourceLineEntity { FileResourceId = resource.Id, LineNumber = index + 1, Text = text })];
        _batchLinks[resource.Id] = linkedBatchIds?.ToList() ?? [];
    }

    public Task<Guid> WriteAsync(
        string fileName, string? originalFolderPath, FileResourceOrigin origin, string content,
        Guid importBatchId, string? converter = null, string? converterOptions = null,
        string? homeDirectoryKey = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Not exercised by ImportFileResourceEndpointsTests.");

    public Task<FileResourceEntity?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_resources.GetValueOrDefault(id));

    public Task<IReadOnlyList<FileResourceLineEntity>> GetLinesAsync(Guid fileResourceId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<FileResourceLineEntity>>(_lines.GetValueOrDefault(fileResourceId) ?? []);

    public Task<PagedItems<FileResourceListItem>> GetPageAsync(
        string? fileName, FileResourceOrigin? origin, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var filtered = _resources.Values.AsEnumerable();
        if (fileName is not null) filtered = filtered.Where(r => string.Equals(r.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        if (origin is not null)   filtered = filtered.Where(r => r.Origin.Parsed == origin);

        var ordered = filtered
            .OrderBy(r => r.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(r => r.LastSeenAtUtc.Parsed)
            .ToList();

        var total             = ordered.Count;
        var effectivePageSize = pageSize == 0 ? total : pageSize;
        var pageItems         = pageSize == 0 ? ordered : [.. ordered.Skip((page - 1) * pageSize).Take(pageSize)];

        var items = pageItems.Select(r => new FileResourceListItem
        {
            Id                      = r.Id,
            FileName                = r.FileName,
            OriginalFolderPath      = r.OriginalFolderPath,
            Origin                  = r.Origin,
            HomeDirectoryKey        = r.HomeDirectoryKey,
            ContentHash             = r.ContentHash,
            LineEnding              = r.LineEnding,
            EndsWithTrailingNewline = r.EndsWithTrailingNewline,
            Converter               = r.Converter,
            ConverterOptions        = r.ConverterOptions,
            FirstSeenAtUtc          = r.FirstSeenAtUtc,
            LastSeenAtUtc           = r.LastSeenAtUtc,
            LinkedBatchCount        = _batchLinks.GetValueOrDefault(r.Id)?.Count ?? 0,
        }).ToList();

        return Task.FromResult(new PagedItems<FileResourceListItem>(items, page, effectivePageSize, total));
    }

    public Task<IReadOnlyList<Guid>> GetBatchIdsAsync(Guid fileResourceId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Guid>>(_batchLinks.GetValueOrDefault(fileResourceId) ?? []);

    public Task<int> PruneAsync(int keepPerFile, CancellationToken cancellationToken = default)
    {
        LastPruneKeepPerFile = keepPerFile;
        return Task.FromResult(PruneResult);
    }
}

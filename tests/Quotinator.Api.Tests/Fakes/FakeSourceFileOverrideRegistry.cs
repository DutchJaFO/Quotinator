using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Data.Repositories;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>In-memory test double for <see cref="ISourceFileOverrideRegistry"/> — avoids requiring a real database in endpoint tests.</summary>
internal sealed class FakeSourceFileOverrideRegistry : ISourceFileOverrideRegistry
{
    private readonly Dictionary<(string FileName, SeedBatchOrigin Origin), SourceFileOverrideEntity> _entries = [];

    public string? LastRegisteredContentHash { get; private set; }
    public string? LastRegisteredSourceBatchId { get; private set; }

    public Task<SourceFileOverrideEntity?> FindAsync(string fileName, SeedBatchOrigin origin, CancellationToken cancellationToken = default)
        => Task.FromResult(_entries.GetValueOrDefault((fileName, origin)));

    public Task RegisterAsync(string fileName, SeedBatchOrigin origin, string contentHash, string? sourceBatchId, CancellationToken cancellationToken = default)
    {
        LastRegisteredContentHash   = contentHash;
        LastRegisteredSourceBatchId = sourceBatchId;
        _entries[(fileName, origin)] = new SourceFileOverrideEntity
        {
            FileName      = fileName,
            Origin        = new SafeValue<SeedBatchOrigin?>(origin.ToString(), origin),
            ContentHash   = contentHash,
            SourceBatchId = sourceBatchId,
        };
        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(string fileName, SeedBatchOrigin origin, CancellationToken cancellationToken = default)
        => Task.FromResult(_entries.Remove((fileName, origin)));
}

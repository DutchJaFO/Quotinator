using Dapper;
using Dapper.Contrib.Extensions;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Enums;
using Quotinator.Data.Helpers;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Repositories;

/// <summary>SQLite implementation of <see cref="IFileResourceRepository"/>.</summary>
/// <remarks>Initialises the repository with the connection factory.</remarks>
/// <param name="factory">Factory used to open SQLite connections.</param>
public sealed class SqliteFileResourceRepository(IDbConnectionFactory factory) : IFileResourceRepository
{
    private readonly IDbConnectionFactory _factory = factory;

    /// <inheritdoc/>
    public async Task<Guid> WriteAsync(
        string fileName, string? originalFolderPath, FileResourceOrigin origin, string content,
        Guid importBatchId, string? converter = null, string? converterOptions = null,
        string? homeDirectoryKey = null, CancellationToken cancellationToken = default)
    {
        var contentHash = EffectiveRuleFileResolver.ComputeContentHash(content);

        using var conn = _factory.CreateConnection();
        conn.Open();

        var existing = await conn.QuerySingleOrDefaultAsync<FileResourceEntity>(
            new CommandDefinition(Sql.FileResources.SelectByContentHash, new { contentHash }, cancellationToken: cancellationToken));

        Guid fileResourceId;
        var now = SafeDateValue.Now;

        if (existing is not null)
        {
            fileResourceId = existing.Id;
            await conn.ExecuteAsync(new CommandDefinition(
                Sql.FileResources.UpdateLastSeenAtUtc,
                new { lastSeenAtUtc = now.Raw, converter, converterOptions, dateModified = now.Raw, id = fileResourceId.ToCanonicalId() },
                cancellationToken: cancellationToken));
        }
        else
        {
            var (lineEnding, endsWithTrailingNewline, lines) = FileContentSplitter.Split(content);

            var fileResource = new FileResourceEntity
            {
                FileName                = fileName,
                OriginalFolderPath      = originalFolderPath,
                Origin                  = new SafeValue<FileResourceOrigin?>(origin.ToString(), origin),
                HomeDirectoryKey        = homeDirectoryKey,
                ContentHash             = contentHash,
                LineEnding              = new SafeValue<LineEndingStyle?>(lineEnding.ToString(), lineEnding),
                EndsWithTrailingNewline = endsWithTrailingNewline,
                Converter               = converter,
                ConverterOptions        = converterOptions,
                FirstSeenAtUtc          = now,
                LastSeenAtUtc           = now,
            };
            fileResourceId = fileResource.Id;
            await conn.InsertAsync(fileResource);

            if (lines.Count > 0)
            {
                var lineEntities = lines.Select((text, index) => new FileResourceLineEntity
                {
                    FileResourceId = fileResourceId,
                    LineNumber     = index + 1,
                    Text           = text,
                });
                await conn.InsertAsync(lineEntities);
            }
        }

        await conn.InsertAsync(new FileResourceBatchEntity
        {
            FileResourceId = fileResourceId,
            ImportBatchId  = importBatchId,
            ImportedAt     = now,
        });

        return fileResourceId;
    }

    /// <inheritdoc/>
    public async Task<FileResourceEntity?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        conn.Open();
        return await conn.QuerySingleOrDefaultAsync<FileResourceEntity>(
            new CommandDefinition(Sql.FileResources.SelectById, new { id = id.ToCanonicalId() }, cancellationToken: cancellationToken));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FileResourceLineEntity>> GetLinesAsync(Guid fileResourceId, CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        conn.Open();
        var lines = await conn.QueryAsync<FileResourceLineEntity>(
            new CommandDefinition(Sql.FileResources.SelectLinesByFileResourceId, new { fileResourceId = fileResourceId.ToCanonicalId() }, cancellationToken: cancellationToken));
        return [.. lines];
    }

    /// <inheritdoc/>
    public async Task<PagedItems<FileResourceListItem>> GetPageAsync(
        string? fileName, FileResourceOrigin? origin, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var filterFileName = fileName is not null;
        var filterOrigin   = origin   is not null;
        var limit          = pageSize == 0 ? -1 : pageSize;
        var offset         = pageSize == 0 ? 0  : (page - 1) * pageSize;
        var param          = new { fileName, origin = origin?.ToString(), pageSize = limit, offset };

        using var conn = _factory.CreateConnection();
        conn.Open();

        var total = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            Sql.FileResources.CountPage(filterFileName, filterOrigin), param, cancellationToken: cancellationToken));

        var items = (await conn.QueryAsync<FileResourceListItem>(new CommandDefinition(
            Sql.FileResources.SelectPage(filterFileName, filterOrigin), param, cancellationToken: cancellationToken))).ToList();

        var effectivePageSize = pageSize == 0 ? items.Count : pageSize;
        return new PagedItems<FileResourceListItem>(items, page, effectivePageSize, total);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Guid>> GetBatchIdsAsync(Guid fileResourceId, CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        conn.Open();
        var ids = await conn.QueryAsync<Guid>(new CommandDefinition(
            Sql.FileResources.SelectBatchIdsForFileResource, new { fileResourceId = fileResourceId.ToCanonicalId() }, cancellationToken: cancellationToken));
        return [.. ids];
    }

    /// <inheritdoc/>
    public async Task<int> PruneAsync(int keepPerFile, CancellationToken cancellationToken = default)
    {
        using var conn = _factory.CreateConnection();
        conn.Open();

        var idsToPrune = (await conn.QueryAsync<Guid>(
            new CommandDefinition(Sql.FileResources.SelectIdsBeyondRetentionPerFileName, new { keepPerFile }, cancellationToken: cancellationToken))).ToList();

        if (idsToPrune.Count == 0) return 0;

        // Cascading deletes to Import_FileResourceLine/Import_FileResourceBatch rely on the schema's
        // own ON DELETE CASCADE, which SQLite only enforces when foreign_keys is ON for the connection
        // issuing the DELETE — off by default per connection, so it's turned on here explicitly rather
        // than assumed from ambient state.
        await conn.ExecuteAsync(new CommandDefinition("PRAGMA foreign_keys = ON;", cancellationToken: cancellationToken));
        await conn.ExecuteAsync(new CommandDefinition(
            Sql.FileResources.DeleteByIds,
            new { ids = idsToPrune.Select(id => id.ToCanonicalId()) },
            cancellationToken: cancellationToken));

        return idsToPrune.Count;
    }
}

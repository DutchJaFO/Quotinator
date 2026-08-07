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

/// <summary>SQLite implementation of <see cref="ISourceFileOverrideRegistry"/>.</summary>
/// <remarks>Initialises the registry with the connection factory.</remarks>
/// <param name="factory">Factory used to open SQLite connections.</param>
public sealed class SourceFileOverrideRegistry(IDbConnectionFactory factory) : SqliteRepositoryBase<SourceFileOverrideEntity>(factory), ISourceFileOverrideRegistry
{

    /// <inheritdoc/>
    public async Task<SourceFileOverrideEntity?> FindAsync(string fileName, SeedBatchOrigin origin, CancellationToken cancellationToken = default)
    {
        using var conn = Factory.CreateConnection();
        conn.Open();
        var command = new CommandDefinition(
            Sql.SystemSourceFileOverrides.SelectByFileNameAndOrigin,
            new { fileName, origin = origin.ToString() },
            cancellationToken: cancellationToken);
        return await conn.QuerySingleOrDefaultAsync<SourceFileOverrideEntity>(command);
    }

    /// <inheritdoc/>
    public async Task RegisterAsync(string fileName, SeedBatchOrigin origin, string contentHash, string? sourceBatchId, CancellationToken cancellationToken = default)
    {
        using var conn = Factory.CreateConnection();
        conn.Open();

        var existing = await FindAsync(fileName, origin, cancellationToken);
        var originValue = new SafeValue<SeedBatchOrigin?>(origin.ToString(), origin);

        if (existing is not null)
        {
            await conn.UpdateAsync(new SourceFileOverrideEntity
            {
                Id            = existing.Id,
                FileName      = fileName,
                Origin        = originValue,
                ContentHash   = contentHash,
                SourceBatchId = sourceBatchId,
                DateCreated   = existing.DateCreated,
                DateModified  = SafeDateValue.Now,
            });
            return;
        }

        await conn.InsertAsync(new SourceFileOverrideEntity
        {
            FileName      = fileName,
            Origin        = originValue,
            ContentHash   = contentHash,
            SourceBatchId = sourceBatchId,
        });
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveAsync(string fileName, SeedBatchOrigin origin, CancellationToken cancellationToken = default)
    {
        var existing = await FindAsync(fileName, origin, cancellationToken);
        if (existing is null) return false;

        using var conn = Factory.CreateConnection();
        conn.Open();
        var command = new CommandDefinition(
            RepositorySql.SoftDelete(TableName),
            new { now = SafeDateValue.Now.Raw, id = existing.Id.ToCanonicalId() },
            cancellationToken: cancellationToken);
        await conn.ExecuteAsync(command);
        return true;
    }
}

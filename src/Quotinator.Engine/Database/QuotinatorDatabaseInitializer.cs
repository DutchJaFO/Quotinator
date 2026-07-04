using Dapper;
using Dapper.Contrib.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Quotinator.Core.Import;
using Quotinator.Core.Models;
using Quotinator.Data.Connections;
using Quotinator.Data.Database;
using Quotinator.Data.Entities;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Data.Queries;
using Quotinator.Data.Repositories;
using Quotinator.Engine.Entities;
using Quotinator.Engine.Repositories;

namespace Quotinator.Engine.Database;

/// <summary>
/// Quotinator-specific database initialiser. Extends <see cref="DatabaseInitializer"/> with
/// seeding logic for Quotinator domain tables (Quotes, Sources, Characters, People, Genres).
/// </summary>
public sealed class QuotinatorDatabaseInitializer : DatabaseInitializer
{
    private readonly IReadOnlyList<SeedBatch>    _batches;
    private readonly IImportBatchRepository      _importBatches;
    private readonly ISourceCacheUpdater         _sourceCacheUpdater;
    private readonly bool                        _autoUpdateSources;

    // API genre tag → database enum name (matches Genre enum values).
    private static readonly IReadOnlyDictionary<string, string> GenreApiToDb =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["action"]      = "Action",
            ["adventure"]   = "Adventure",
            ["animation"]   = "Animation",
            ["comedy"]      = "Comedy",
            ["drama"]       = "Drama",
            ["fantasy"]     = "Fantasy",
            ["fiction"]     = "Fiction",
            ["horror"]      = "Horror",
            ["mystery"]     = "Mystery",
            ["non-fiction"] = "NonFiction",
            ["romance"]     = "Romance",
            ["sci-fi"]      = "SciFi",
            ["thriller"]    = "Thriller",
        };

    /// <summary>Initialises the instance with all dependencies required for Quotinator seeding.</summary>
    public QuotinatorDatabaseInitializer(
        IDbConnectionFactory           factory,
        DatabaseOptions                options,
        IReadOnlyList<SchemaMigration> migrations,
        IReadOnlyList<SeedBatch>       batches,
        IImportBatchRepository         importBatches,
        ISystemAuditWriter             auditWriter,
        ICallerContext                 callerContext,
        ILogger<DatabaseInitializer>   logger,
        ISourceCacheUpdater            sourceCacheUpdater,
        bool                           autoUpdateSources,
        SchemaBaseline?                baseline = null)
        : base(factory, options, migrations, auditWriter, callerContext, logger, baseline)
    {
        _batches            = batches;
        _importBatches      = importBatches;
        _sourceCacheUpdater = sourceCacheUpdater;
        _autoUpdateSources  = autoUpdateSources;
    }

    /// <inheritdoc/>
    protected override async Task OnInitialisedAsync(SqliteConnection connection)
    {
        var effectiveBatches = (await ResolveEffectiveBatchesAsync(forceRefresh: false)).EffectiveBatches;
        await SeedIfEmptyAsync(connection, effectiveBatches);
        await ReSeedGenresIfEmptyAsync(connection, effectiveBatches);
        await LogDatabaseStatsAsync(connection);
    }

    /// <inheritdoc/>
    protected override async Task OnReseedAsync(SqliteConnection connection, bool forceSourceRefresh)
    {
        var effectiveBatches = (await ResolveEffectiveBatchesAsync(forceSourceRefresh)).EffectiveBatches;
        var totalFiles = effectiveBatches.Sum(b => b.Files.Count);
        Logger.LogInformation("[Database - Seed] reseed requested — clearing all data and reimporting from {Count} source file(s)...", totalFiles);

        await SharedSeedLock.WaitAsync();
        try
        {
            await TruncateDataAsync(connection);
            await SeedIfEmptyInternalAsync(connection, effectiveBatches);
        }
        finally
        {
            SharedSeedLock.Release();
        }

        await LogDatabaseStatsAsync(connection);
        await AuditWriter.WriteAsync(new SystemAuditEntry
        {
            TableName   = "Database",
            Operation   = AuditOperation.Reseed,
            Agent       = CallerContext.Agent,
            PerformedAt = DateTime.UtcNow,
        });
        Logger.LogInformation("[Database - Seed] reseed complete");
    }

    /// <inheritdoc/>
    protected override async Task OnResetAsync(SqliteConnection connection, bool preserveSchemaVersion, bool forceSourceRefresh)
    {
        var effectiveBatches = (await ResolveEffectiveBatchesAsync(forceSourceRefresh)).EffectiveBatches;
        var totalFiles = effectiveBatches.Sum(b => b.Files.Count);
        Logger.LogInformation("[Database - Init] reset requested — rebuilding schema and reimporting from {Count} source file(s)...", totalFiles);

        await SharedSeedLock.WaitAsync();
        try
        {
            await DropAndRebuildAsync(connection, preserveSchemaVersion);
            await SeedIfEmptyInternalAsync(connection, effectiveBatches);
        }
        finally
        {
            SharedSeedLock.Release();
        }

        await LogDatabaseStatsAsync(connection);
        await AuditWriter.WriteAsync(new SystemAuditEntry
        {
            TableName   = "Database",
            Operation   = AuditOperation.Reset,
            Agent       = CallerContext.Agent,
            PerformedAt = DateTime.UtcNow,
        });
        Logger.LogInformation("[Database - Init] reset complete");
    }

    /// <inheritdoc/>
    public override async Task<SeedPreviewResult> PreviewSeedAsync()
    {
        // Preview reflects whatever is already cached on disk — it never triggers a network call,
        // even when Quotinator__AutoUpdateSources is true, so calling it has no side effects.
        var resolution       = await ResolveEffectiveBatchesAsync(forceRefresh: false, allowNetworkOverride: false);
        var effectiveBatches = resolution.EffectiveBatches;
        var resultsByName    = resolution.Results.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);

        var filePreviews = new List<SeedFilePreview>();
        var duplicates   = new List<SeedDuplicateRecord>();
        var seenIds      = new Dictionary<string, string>(StringComparer.Ordinal);
        var totalQuotes  = 0;

        foreach (var batch in effectiveBatches)
        {
            foreach (var seedFile in batch.Files)
            {
                var fileName = Path.GetFileName(seedFile.FilePath);
                var (quotes, issue) = LoadQuotesFromFile(seedFile.FilePath);
                var refreshResult = resultsByName.GetValueOrDefault(fileName);
                filePreviews.Add(new SeedFilePreview(fileName, quotes.Count, refreshResult?.Outcome, refreshResult?.LastRefreshedAtUtc, issue));
                totalQuotes += quotes.Count;

                foreach (var q in quotes)
                {
                    if (seenIds.TryGetValue(q.Id, out var firstFile))
                    {
                        duplicates.Add(new SeedDuplicateRecord(
                            "quote", q.Id, TruncateLabel(q.QuoteText),
                            Path.GetFileName(firstFile), fileName,
                            batch.Policy.ForQuotes));
                    }
                    else
                    {
                        seenIds[q.Id] = seedFile.FilePath;
                    }
                }
            }
        }

        return new SeedPreviewResult(
            filePreviews,
            duplicates,
            totalQuotes,
            seenIds.Count);
    }

    /// <inheritdoc/>
    public override async Task<SourceCacheResolution> RefreshSourcesAsync(bool force = false)
        => await _sourceCacheUpdater.ResolveAsync(_batches, _autoUpdateSources, force);

    /// <summary>
    /// Resolves <see cref="_batches"/> to their effective form for this call via
    /// <see cref="_sourceCacheUpdater"/>. <see cref="_batches"/> itself is never mutated — this
    /// singleton is shared across concurrent Preview/Reseed/Reset calls, so each caller gets its
    /// own local effective list instead of a shared field that could race.
    /// </summary>
    /// <param name="forceRefresh">Bypasses the TTL check for every candidate entry; ignored when network access is not allowed.</param>
    /// <param name="allowNetworkOverride">
    /// Overrides <see cref="_autoUpdateSources"/> for this call. Used by <see cref="PreviewSeedAsync"/>
    /// to guarantee it never makes a network call regardless of configuration.
    /// </param>
    private async Task<SourceCacheResolution> ResolveEffectiveBatchesAsync(bool forceRefresh, bool? allowNetworkOverride = null)
    {
        var allowNetwork = allowNetworkOverride ?? _autoUpdateSources;
        return await _sourceCacheUpdater.ResolveAsync(_batches, allowNetwork, forceRefresh);
    }

    private async Task SeedIfEmptyAsync(SqliteConnection connection, IReadOnlyList<SeedBatch> effectiveBatches)
    {
        await SharedSeedLock.WaitAsync();
        try
        {
            await SeedIfEmptyInternalAsync(connection, effectiveBatches);
        }
        finally
        {
            SharedSeedLock.Release();
        }
    }

    private async Task SeedIfEmptyInternalAsync(SqliteConnection connection, IReadOnlyList<SeedBatch> effectiveBatches)
    {
        var count = await connection.ExecuteScalarAsync<int>(Sql.Quotes.CountAll);
        if (count > 0) return;

        if (effectiveBatches.Count == 0)
        {
            Logger.LogWarning("[Database - Seed] no source files configured — database will be empty");
            return;
        }

        LastSeedDuplicates = [];

        var seenIds        = new Dictionary<string, string>(StringComparer.Ordinal);
        var sourceIndex    = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var characterIndex = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var personIndex    = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var duplicates     = new List<SeedDuplicateRecord>();

        var now = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat);

        foreach (var batch in effectiveBatches)
        {
            foreach (var seedFile in batch.Files)
            {
                var fileName    = Path.GetFileName(seedFile.FilePath);
                var (quotes, _) = LoadQuotesFromFile(seedFile.FilePath);
                var importBatch = await CreateImportBatchAsync(batch, seedFile);

                Logger.LogInformation("[Database - Seed] importing {Count} quotes from {File} ({Batch})...",
                    quotes.Count, fileName, batch.Label);

                var fileQuoteCount = 0;

                foreach (var q in quotes)
                {
                    if (seenIds.TryGetValue(q.Id, out var firstFile))
                    {
                        duplicates.Add(new SeedDuplicateRecord(
                            "quote", q.Id, TruncateLabel(q.QuoteText),
                            Path.GetFileName(firstFile), fileName,
                            batch.Policy.ForQuotes));

                        if (batch.Policy.ForQuotes == DuplicateResolutionPolicy.Skip)
                        {
                            Logger.LogDebug(
                                "[Database - Seed] skipping duplicate quote {Id} in {File} (first seen in {First})",
                                q.Id, fileName, Path.GetFileName(firstFile));
                            continue;
                        }

                        Logger.LogDebug(
                            "[Database - Seed] overwriting duplicate quote {Id} in {File} (was {First})",
                            q.Id, fileName, Path.GetFileName(firstFile));

                        await connection.ExecuteAsync(Sql.QuoteGenres.DeleteForQuote,      new { id = q.Id });
                        await connection.ExecuteAsync(Sql.QuoteTranslations.DeleteForQuote, new { id = q.Id });

                        var owSourceId    = await GetOrCreateSourceAsync(connection, q, sourceIndex, importBatch.Id);
                        var owCharacterId = await GetOrCreateCharacterAsync(connection, q, owSourceId, characterIndex, importBatch.Id);
                        var owPersonId    = await GetOrCreatePersonAsync(connection, q, personIndex, importBatch.Id);

                        await connection.ExecuteAsync(
                            Sql.Quotes.UpdateOnOverwrite,
                            new
                            {
                                text    = q.QuoteText,
                                lang    = q.OriginalLanguage,
                                sid     = owSourceId,
                                cid     = owCharacterId,
                                pid     = owPersonId,
                                batchId = importBatch.Id,
                                mod     = now,
                                id      = q.Id
                            });

                        seenIds[q.Id] = seedFile.FilePath;

                        var owQuoteId = Guid.Parse(q.Id);
                        await InsertTranslationsAsync(connection, q, owQuoteId, owSourceId, now);
                        await InsertGenresAsync(connection, q, owQuoteId, now);
                        continue;
                    }

                    seenIds[q.Id] = seedFile.FilePath;

                    var sourceId    = await GetOrCreateSourceAsync(connection, q, sourceIndex, importBatch.Id);
                    var characterId = await GetOrCreateCharacterAsync(connection, q, sourceId, characterIndex, importBatch.Id);
                    var personId    = await GetOrCreatePersonAsync(connection, q, personIndex, importBatch.Id);
                    var quoteId     = Guid.Parse(q.Id);

                    await connection.ExecuteAsync(
                        Sql.Quotes.Insert,
                        new
                        {
                            Id               = q.Id,
                            QuoteText        = q.QuoteText,
                            OriginalLanguage = q.OriginalLanguage,
                            SourceId         = sourceId,
                            CharacterId      = characterId,
                            PersonId         = personId,
                            ImportBatchId    = importBatch.Id,
                            DateCreated      = now
                        });

                    await InsertTranslationsAsync(connection, q, quoteId, sourceId, now);
                    await InsertGenresAsync(connection, q, quoteId, now);
                    fileQuoteCount++;
                }

                await _importBatches.UpdateRecordCountAsync(importBatch.Id, fileQuoteCount);

                await AuditWriter.WriteAsync(new SystemAuditEntry
                {
                    TableName   = "Quotes",
                    RecordId    = importBatch.Id.ToString("D").ToUpperInvariant(),
                    Operation   = AuditOperation.BulkInsert,
                    Agent       = CallerContext.Agent,
                    PerformedAt = DateTime.UtcNow,
                }, connection);
            }
        }

        LastSeedDuplicates = duplicates;

        var dupCount = duplicates.Count;
        Logger.LogInformation(
            "[Database - Seed] seeding complete — {Unique} unique quotes from {Total} total ({Dups} duplicate{S})",
            seenIds.Count, seenIds.Count + dupCount, dupCount, dupCount == 1 ? "" : "s");
    }

    private async Task ReSeedGenresIfEmptyAsync(SqliteConnection connection, IReadOnlyList<SeedBatch> effectiveBatches)
    {
        var genreCount = await connection.ExecuteScalarAsync<int>(Sql.QuoteGenres.CountAll);
        if (genreCount > 0) return;

        var quoteCount = await connection.ExecuteScalarAsync<int>(Sql.Quotes.CountAll);
        if (quoteCount == 0) return;

        if (effectiveBatches.Count == 0)
        {
            Logger.LogWarning("[Database - Seed] cannot re-seed genres — no source files configured");
            return;
        }

        Logger.LogInformation("[Database - Seed] re-seeding genres from source files...");

        var now      = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat);
        var inserted = 0;

        foreach (var batch in effectiveBatches)
        {
            foreach (var seedFile in batch.Files)
            {
                var (quotes, _) = LoadQuotesFromFile(seedFile.FilePath);
                foreach (var q in quotes)
                {
                    foreach (var genre in q.Genres)
                    {
                        if (TryNormaliseGenre(genre, out var g))
                        {
                            await connection.ExecuteAsync(
                                Sql.QuoteGenres.InsertWithExistsGuard,
                                new { Id = Guid.NewGuid().ToString(), QuoteId = q.Id, Genre = g.ToString(), DateCreated = now });
                            inserted++;
                        }
                    }
                }
            }
        }

        Logger.LogInformation("[Database - Seed] genre re-seed complete — {Count} genre rows processed", inserted);
    }

    private async Task LogDatabaseStatsAsync(SqliteConnection connection)
    {
        QuoteCount     = await connection.ExecuteScalarAsync<int>(Sql.Quotes.CountActive);
        SourceCount    = await connection.ExecuteScalarAsync<int>(Sql.Sources.CountActive);
        CharacterCount = await connection.ExecuteScalarAsync<int>(Sql.Characters.CountActive);
        PeopleCount    = await connection.ExecuteScalarAsync<int>(Sql.People.CountActive);

        Logger.LogInformation(
            "[Database - Stats] {Quotes} quotes  {Sources} sources  {Characters} characters  {People} people",
            QuoteCount, SourceCount, CharacterCount, PeopleCount);
    }

    private async Task InsertTranslationsAsync(
        SqliteConnection connection, SourceQuote q, Guid quoteId, Guid sourceId, string now)
    {
        foreach (var (lang, t) in q.Translations)
        {
            await connection.ExecuteAsync(
                Sql.QuoteTranslations.Insert,
                new
                {
                    Id        = Guid.NewGuid().ToString(),
                    QuoteId   = quoteId.ToString(),
                    Language  = lang,
                    QuoteText = t.QuoteText,
                    DateCreated = now
                });

            if (t.Source is not null)
            {
                var exists = await connection.ExecuteScalarAsync<int>(
                    Sql.SourceTranslations.CountForSource,
                    new { sid = sourceId, lang });
                if (exists == 0)
                    await connection.InsertAsync(new SourceTranslation
                    {
                        SourceId = sourceId,
                        Language = lang,
                        Title    = t.Source
                    });
            }
        }
    }

    private async Task InsertGenresAsync(SqliteConnection connection, SourceQuote q, Guid quoteId, string now)
    {
        foreach (var genre in q.Genres)
        {
            if (TryNormaliseGenre(genre, out var g))
            {
                await connection.ExecuteAsync(
                    Sql.QuoteGenres.Insert,
                    new { Id = Guid.NewGuid().ToString(), QuoteId = quoteId.ToString(), Genre = g.ToString(), DateCreated = now });
            }
        }
    }

    private async Task<ImportBatch> CreateImportBatchAsync(SeedBatch seedBatch, SeedFile seedFile)
    {
        var type = DetermineType(seedBatch.Origin);
        var batch = new ImportBatch
        {
            Name       = Path.GetFileName(seedFile.FilePath),
            Type       = type.ToString(),
            Url        = seedFile.Url,
            ImportedAt = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat)
        };
        await _importBatches.InsertAsync(batch);
        return batch;
    }

    // Origin decides the type, not URL presence — a user-imports-folder file that happens to
    // declare its own url/github manifest entry is still UserSeed, never Seed, so provenance
    // always reflects which folder the file was actually scanned from. A bundled file is always
    // Seed regardless of whether it has a URL — "System" is reserved for the database's own
    // System_-prefixed infrastructure tables (see Sql.Schema.GetUserTables), not for quote
    // content provenance; internally-authored bundled content (e.g. quotinator-curated.json) is
    // still replaceable, re-seeded content, just like externally-sourced bundled content.
    private static ImportBatchType DetermineType(SeedBatchOrigin origin) =>
        origin == SeedBatchOrigin.UserImports
            ? ImportBatchType.UserSeed
            : ImportBatchType.Seed;

    private static async Task<Guid> GetOrCreateSourceAsync(
        SqliteConnection connection, SourceQuote q, Dictionary<string, Guid> index, Guid importBatchId)
    {
        var typeStr = NormaliseType(q.Type);
        var key     = $"{q.Source}|{typeStr}";
        if (index.TryGetValue(key, out var existing)) return existing;

        var id = Guid.NewGuid();
        await connection.InsertAsync(new Source
        {
            Id            = id,
            Title         = q.Source,
            Type          = new SafeValue<QuoteType?>(typeStr, ParseQuoteType(q.Type)),
            Date          = string.IsNullOrEmpty(q.Date) ? SafeDateValue.Empty : new SafeValue<DateTime?>(q.Date, null),
            ImportBatchId = importBatchId
        });

        index[key] = id;
        return id;
    }

    private static async Task<Guid?> GetOrCreateCharacterAsync(
        SqliteConnection connection, SourceQuote q, Guid sourceId, Dictionary<string, Guid> index, Guid importBatchId)
    {
        if (string.IsNullOrWhiteSpace(q.Character)) return null;

        var key = $"{sourceId}|{q.Character}";
        if (index.TryGetValue(key, out var existing)) return existing;

        var id = Guid.NewGuid();
        await connection.InsertAsync(new Character
        {
            Id            = id,
            SourceId      = sourceId,
            Name          = q.Character,
            ImportBatchId = importBatchId
        });

        index[key] = id;
        return id;
    }

    private static async Task<Guid?> GetOrCreatePersonAsync(
        SqliteConnection connection, SourceQuote q, Dictionary<string, Guid> index, Guid importBatchId)
    {
        if (string.IsNullOrWhiteSpace(q.Author)) return null;

        if (index.TryGetValue(q.Author, out var existing)) return existing;

        var id = Guid.NewGuid();
        await connection.InsertAsync(new Person
        {
            Id            = id,
            Name          = q.Author,
            ImportBatchId = importBatchId
        });

        index[q.Author] = id;
        return id;
    }

    private static string NormaliseType(string raw) => ParseQuoteType(raw).ToString();

    private static QuoteType ParseQuoteType(string raw)
        => Enum.TryParse<QuoteType>(raw, ignoreCase: true, out var t) ? t : QuoteType.Unknown;

    private static bool TryNormaliseGenre(string raw, out Genre result)
    {
        if (GenreApiToDb.TryGetValue(raw, out var dbName) &&
            Enum.TryParse<Genre>(dbName, out result))
            return true;
        result = default;
        return false;
    }

    private (List<SourceQuote> Quotes, SeedFileIssue? Issue) LoadQuotesFromFile(string filePath)
    {
        if (!File.Exists(filePath)) return ([], SeedFileIssue.Missing);

        var json = File.ReadAllText(filePath);
        if (SourceQuoteFileReader.TryParse(json, out var quotes)) return (quotes!, null);

        Logger.LogWarning("[Database - Seed] {File} is empty or not valid JSON — skipping", Path.GetFileName(filePath));
        return ([], SeedFileIssue.InvalidJson);
    }

    private static string TruncateLabel(string text, int maxLen = 60)
        => text.Length <= maxLen ? text : text[..maxLen] + "…";
}

using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Dapper.Contrib.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Import;
using Quotinator.Data.Models;
using Quotinator.Data.Paths;
using Quotinator.Data.Queries;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Database;

/// <summary>
/// Runs schema migrations and seeds the database from one or more source files on first run.
/// Call <see cref="InitialiseAsync"/> once at startup before serving requests.
/// </summary>
public sealed class DatabaseInitializer : IDatabaseInitializer
{
    private readonly IDbConnectionFactory            _factory;
    private readonly DatabaseOptions                 _options;
    private readonly IReadOnlyList<SchemaMigration>  _migrations;
    private readonly IReadOnlyList<SeedBatch>        _batches;
    private readonly IImportBatchRepository          _importBatches;
    private readonly IAuditWriter                    _auditWriter;
    private readonly ICallerContext                  _callerContext;
    private readonly ILogger<DatabaseInitializer>    _logger;

    private List<SeedDuplicateRecord> _lastSeedDuplicates = [];

    /// <inheritdoc/>
    public int SchemaVersion { get; private set; }

    /// <inheritdoc/>
    public int QuoteCount { get; private set; }

    /// <inheritdoc/>
    public int SourceCount { get; private set; }

    /// <inheritdoc/>
    public int CharacterCount { get; private set; }

    /// <inheritdoc/>
    public int PeopleCount { get; private set; }

    /// <inheritdoc/>
    public string? MigrationApplied { get; private set; }

    /// <inheritdoc/>
    public IReadOnlyList<SeedDuplicateRecord> LastSeedDuplicates => _lastSeedDuplicates;

    // Guards against concurrent seeding when multiple WebApplicationFactory instances start in
    // the same process (e.g. parallel MSTest runs). Each waiter re-checks COUNT(*) after
    // acquiring the lock and skips seeding if the previous holder already populated the DB.
    private static readonly SemaphoreSlim _seedLock = new(1, 1);

    // API genre tag → database enum name (matches Genre enum values).
    // Kept local to this class because the seeder is the only Data consumer of this mapping;
    // API-layer query normalisation uses the copy in Quotinator.Core.Helpers.InputValidation.
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

    /// <summary>Initialises the instance with the connection factory and the ordered list of source batches to seed from.</summary>
    /// <param name="factory">Factory used to open SQLite connections.</param>
    /// <param name="options">Database file paths and settings.</param>
    /// <param name="migrations">Ordered, append-only list of schema migrations to apply.</param>
    /// <param name="batches">Source file batches in import order, each with its resolved duplicate-resolution policy.</param>
    /// <param name="importBatches">Repository used to record provenance for each seeded file.</param>
    /// <param name="auditWriter">Writes an audit entry on completion of reseed and reset operations.</param>
    /// <param name="callerContext">Provides the agent identifier for audit entries.</param>
    /// <param name="logger">Logger for startup diagnostics.</param>
    public DatabaseInitializer(
        IDbConnectionFactory            factory,
        DatabaseOptions                 options,
        IReadOnlyList<SchemaMigration>  migrations,
        IReadOnlyList<SeedBatch>        batches,
        IImportBatchRepository          importBatches,
        IAuditWriter                    auditWriter,
        ICallerContext                  callerContext,
        ILogger<DatabaseInitializer>    logger)
    {
        _factory       = factory;
        _options       = options;
        _migrations    = migrations;
        _batches       = batches;
        _importBatches = importBatches;
        _auditWriter   = auditWriter;
        _callerContext = callerContext;
        _logger        = logger;
    }

    /// <inheritdoc/>
    public async Task InitialiseAsync()
    {
        MigrateFilenameIfNeeded();

        using var connection = (SqliteConnection)_factory.CreateConnection();
        await connection.OpenAsync();

        EnableWal(connection);
        await ApplyMigrationsAsync(connection);
        await SeedIfEmptyAsync(connection);
        await ReSeedGenresIfEmptyAsync(connection);
        await LogDatabaseStatsAsync(connection);
    }

    // -------------------------------------------------------------------------
    #region File management

    private void MigrateFilenameIfNeeded()
    {
        var dataDir    = Path.GetDirectoryName(_options.DbPath)!;
        var legacyPath = Path.Combine(dataDir, DataPaths.LegacyDatabaseFile);
        if (!File.Exists(legacyPath) || File.Exists(_options.DbPath)) return;

        _logger.LogInformation("[Database - Init] migrating legacy filename quotes.db → {NewName}", Path.GetFileName(_options.DbPath));
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var src = legacyPath + suffix;
            var dst = _options.DbPath + suffix;
            if (!File.Exists(src)) continue;
            _logger.LogInformation("[Database - Init] moving {Src} → {Dst}", Path.GetFileName(src), Path.GetFileName(dst));
            File.Move(src, dst);
        }
        _logger.LogInformation("[Database - Init] filename migration complete → {Path}", _options.DbPath);
    }

    private void CreateBackup(SqliteConnection connection, int fromVersion)
    {
        Directory.CreateDirectory(_options.BackupsPath);
        var timestamp  = DateTime.UtcNow.ToString("yyyyMMddTHHmmss");
        var backupName = $"{Path.GetFileNameWithoutExtension(_options.DbPath)}_v{fromVersion}_{timestamp}Z.db";
        var backupPath = Path.Combine(_options.BackupsPath, backupName);

        _logger.LogInformation("[Database - Backup] backing up v{Version} → {Path}", fromVersion, backupPath);
        using var dest = new SqliteConnection($"Data Source={backupPath}");
        dest.Open();
        connection.BackupDatabase(dest);
        _logger.LogInformation("[Database - Backup] backup complete");
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Migrations

    private static void EnableWal(SqliteConnection connection)
        => connection.Execute("PRAGMA journal_mode=WAL;");

    private async Task ApplyMigrationsAsync(SqliteConnection connection)
    {
        await connection.ExecuteAsync(Sql.Schema.CreateTable);

        var current = await connection.ExecuteScalarAsync<int>(Sql.Schema.GetCurrentVersion);

        if (current >= _migrations.Count)
        {
            SchemaVersion = current;
            _logger.LogInformation("[Database - Init] schema is up to date at version {Version}", current);
            return;
        }

        if (current == 0)
        {
            _logger.LogInformation("[Database - Init] creating schema...");
        }
        else
        {
            _logger.LogInformation(
                "[Database - Init] applying {Count} pending migration(s) (version {Current} → {Target})...",
                _migrations.Count - current, current, _migrations.Count);
            CreateBackup(connection, current);
        }

        for (var i = current; i < _migrations.Count; i++)
        {
            using var tx = connection.BeginTransaction();
            try
            {
                await connection.ExecuteAsync(_migrations[i].Sql, transaction: tx);
                await connection.ExecuteAsync(
                    Sql.Schema.InsertVersion,
                    new { v = i + 1, at = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat) },
                    transaction: tx);
                await tx.CommitAsync();
            }
            catch (SqliteException ex) when (IsKnownMigrationError(ex, i + 1))
            {
                // A prior partial run already applied this migration's DDL but did not record
                // the version. Roll back the failed attempt (a no-op in practice — the column/table
                // was already there before the transaction started), then record the version
                // outside the transaction so the loop advances.
                await tx.RollbackAsync();
                _logger.LogWarning(
                    "[Database - Init] migration {Version} was previously partially applied — " +
                    "recording version and continuing. If data appears missing, use Reset Database.",
                    i + 1);
                await connection.ExecuteAsync(
                    Sql.Schema.InsertVersion,
                    new { v = i + 1, at = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat) });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(
                    ex,
                    "[Database - Init] migration {Version} failed — rolled back to version {Current}. " +
                    "Resolve the issue and restart the application.",
                    i + 1, i);
                throw;
            }
        }

        SchemaVersion = _migrations.Count;
        if (current > 0)
            MigrationApplied = $"v{current} → v{_migrations.Count}";
        _logger.LogInformation(
            "[Database - Init] schema {Action} at version {Version}",
            current == 0 ? "created" : "updated", _migrations.Count);
    }

    // Returns true only for explicitly documented per-version known errors.
    // Each case is tied to a specific migration version number — a similar error in a future
    // migration will NOT be silently recovered unless it is explicitly added here.
    // Any version or error not listed propagates so the app does not start in a broken state.
    private static bool IsKnownMigrationError(SqliteException ex, int migrationVersion)
        => migrationVersion switch
        {
            // Migration003 added ImportBatchId columns via ALTER TABLE ADD COLUMN. The broken
            // ResetAsync in v1.5.x–v1.6.1 could apply this migration's DDL without recording
            // the version, leaving the columns present but SchemaVersion stuck at 2. On the
            // next startup the ALTER TABLE fails because the column already exists.
            3 => ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

    // Discovers all user tables at runtime and drops them. This ensures ResetAsync always
    // drops every table, including ones added by future migrations, without requiring a
    // manual update to a hardcoded list.
    // Table names come from sqlite_master (system metadata, not user input) — string
    // interpolation is safe here. Brackets handle any unusual table names.
    // Caller must disable FK checks before calling (PRAGMA foreign_keys = OFF).
    private static async Task DropAllTablesAsync(SqliteConnection connection)
    {
        var tables = (await connection.QueryAsync<string>(Sql.Schema.GetUserTables)).ToList();
        foreach (var table in tables)
            await connection.ExecuteAsync($"DROP TABLE IF EXISTS [{table}];");
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Seeding

    /// <inheritdoc/>
    public async Task ReseedAsync()
    {
        using var connection = (SqliteConnection)_factory.CreateConnection();
        await connection.OpenAsync();

        var totalFiles = _batches.Sum(b => b.Files.Count);
        _logger.LogInformation("[Database - Seed] reseed requested — clearing all data and reimporting from {Count} source file(s)...", totalFiles);

        await _seedLock.WaitAsync();
        try
        {
            await TruncateDataAsync(connection);
            await SeedIfEmptyInternalAsync(connection);
        }
        finally
        {
            _seedLock.Release();
        }

        await LogDatabaseStatsAsync(connection);
        await _auditWriter.WriteAsync(new AuditEntry
        {
            TableName   = "Database",
            Operation   = AuditOperation.Reseed,
            Agent       = _callerContext.Agent,
            PerformedAt = DateTime.UtcNow,
        });
        _logger.LogInformation("[Database - Seed] reseed complete");
    }

    /// <inheritdoc/>
    public async Task ResetAsync()
    {
        using var connection = (SqliteConnection)_factory.CreateConnection();
        await connection.OpenAsync();

        var totalFiles = _batches.Sum(b => b.Files.Count);
        _logger.LogInformation("[Database - Init] reset requested — rebuilding schema and reimporting from {Count} source file(s)...", totalFiles);

        await _seedLock.WaitAsync();
        try
        {
            await connection.ExecuteAsync("PRAGMA foreign_keys = OFF;");
            await connection.ExecuteAsync(Sql.Schema.DeleteAll);
            await DropAllTablesAsync(connection);
            await connection.ExecuteAsync("PRAGMA foreign_keys = ON;");
            await ApplyMigrationsAsync(connection);
            await SeedIfEmptyInternalAsync(connection);
        }
        finally
        {
            _seedLock.Release();
        }

        await LogDatabaseStatsAsync(connection);
        await _auditWriter.WriteAsync(new AuditEntry
        {
            TableName   = "Database",
            Operation   = AuditOperation.Reset,
            Agent       = _callerContext.Agent,
            PerformedAt = DateTime.UtcNow,
        });
        _logger.LogInformation("[Database - Init] reset complete");
    }

    /// <inheritdoc/>
    public Task<SeedPreviewResult> PreviewSeedAsync()
    {
        var filePreviews = new List<SeedFilePreview>();
        var duplicates   = new List<SeedDuplicateRecord>();
        var seenIds      = new Dictionary<string, string>(StringComparer.Ordinal);
        var totalQuotes  = 0;

        foreach (var batch in _batches)
        {
            foreach (var file in batch.Files)
            {
                var fileName = Path.GetFileName(file);
                var quotes   = LoadQuotesFromFile(file);
                filePreviews.Add(new SeedFilePreview(fileName, quotes.Count));
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
                        seenIds[q.Id] = file;
                    }
                }
            }
        }

        return Task.FromResult(new SeedPreviewResult(
            filePreviews,
            duplicates,
            totalQuotes,
            seenIds.Count));
    }

    private async Task SeedIfEmptyAsync(SqliteConnection connection)
    {
        await _seedLock.WaitAsync();
        try
        {
            await SeedIfEmptyInternalAsync(connection);
        }
        finally
        {
            _seedLock.Release();
        }
    }

    private static async Task TruncateDataAsync(SqliteConnection connection)
    {
        await connection.ExecuteAsync("PRAGMA foreign_keys = OFF;");
        await connection.ExecuteAsync(Sql.QuoteGenres.DeleteAll);
        await connection.ExecuteAsync(Sql.QuoteTranslations.DeleteAll);
        await connection.ExecuteAsync(Sql.SourceTranslations.DeleteAll);
        await connection.ExecuteAsync(Sql.CharacterTranslations.DeleteAll);
        await connection.ExecuteAsync(Sql.Quotes.DeleteAll);
        await connection.ExecuteAsync(Sql.Characters.DeleteAll);
        await connection.ExecuteAsync(Sql.People.DeleteAll);
        await connection.ExecuteAsync(Sql.Sources.DeleteAll);
        await connection.ExecuteAsync(Sql.ImportBatches.DeleteAll);
        await connection.ExecuteAsync("PRAGMA foreign_keys = ON;");
    }

    private async Task SeedIfEmptyInternalAsync(SqliteConnection connection)
    {
        var count = await connection.ExecuteScalarAsync<int>(Sql.Quotes.CountAll);
        if (count > 0) return;

        if (_batches.Count == 0)
        {
            _logger.LogWarning("[Database - Seed] no source files configured — database will be empty");
            return;
        }

        _lastSeedDuplicates = [];

        // In-memory indices shared across all batches — sources/characters/people are deduped by
        // natural key (title+type, source+name, author name) within a single seeding run.
        var seenIds        = new Dictionary<string, string>(StringComparer.Ordinal);
        var sourceIndex    = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var characterIndex = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var personIndex    = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat);

        foreach (var batch in _batches)
        {
            foreach (var file in batch.Files)
            {
                var fileName    = Path.GetFileName(file);
                var quotes      = LoadQuotesFromFile(file);
                var importBatch = await CreateImportBatchAsync(file, batch.Label);

                _logger.LogInformation("[Database - Seed] importing {Count} quotes from {File} ({Batch})...",
                    quotes.Count, fileName, batch.Label);

                var fileQuoteCount = 0;

                foreach (var q in quotes)
                {
                    if (seenIds.TryGetValue(q.Id, out var firstFile))
                    {
                        _lastSeedDuplicates.Add(new SeedDuplicateRecord(
                            "quote", q.Id, TruncateLabel(q.QuoteText),
                            Path.GetFileName(firstFile), fileName,
                            batch.Policy.ForQuotes));

                        if (batch.Policy.ForQuotes == DuplicateResolutionPolicy.Skip)
                        {
                            _logger.LogDebug(
                                "[Database - Seed] skipping duplicate quote {Id} in {File} (first seen in {First})",
                                q.Id, fileName, Path.GetFileName(firstFile));
                            continue;
                        }

                        // OVERWRITE: delete children first (FK constraint), then update the parent.
                        _logger.LogDebug(
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

                        seenIds[q.Id] = file;

                        var owQuoteId = Guid.Parse(q.Id);
                        await InsertTranslationsAsync(connection, q, owQuoteId, owSourceId, now);
                        await InsertGenresAsync(connection, q, owQuoteId, now);
                        continue;
                    }

                    // First occurrence — normal insert.
                    seenIds[q.Id] = file;

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
            }
        }

        var dupCount = _lastSeedDuplicates.Count;
        _logger.LogInformation(
            "[Database - Seed] seeding complete — {Unique} unique quotes from {Total} total ({Dups} duplicate{S})",
            seenIds.Count, seenIds.Count + dupCount, dupCount, dupCount == 1 ? "" : "s");
    }

    private async Task ReSeedGenresIfEmptyAsync(SqliteConnection connection)
    {
        var genreCount = await connection.ExecuteScalarAsync<int>(Sql.QuoteGenres.CountAll);
        if (genreCount > 0) return;

        var quoteCount = await connection.ExecuteScalarAsync<int>(Sql.Quotes.CountAll);
        if (quoteCount == 0) return;

        if (_batches.Count == 0)
        {
            _logger.LogWarning("[Database - Seed] cannot re-seed genres — no source files configured");
            return;
        }

        _logger.LogInformation("[Database - Seed] re-seeding genres from source files...");

        var now      = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat);
        var inserted = 0;

        foreach (var batch in _batches)
        {
            foreach (var file in batch.Files)
            {
                var quotes = LoadQuotesFromFile(file);
                foreach (var q in quotes)
                {
                    foreach (var genre in q.Genres)
                    {
                        if (TryNormaliseGenre(genre, out var g))
                        {
                            // WHERE EXISTS guards against FK violation when source-file IDs differ
                            // from the IDs already in the database (e.g. after a UUID scheme change).
                            await connection.ExecuteAsync(
                                Sql.QuoteGenres.InsertWithExistsGuard,
                                new { Id = Guid.NewGuid().ToString(), QuoteId = q.Id, Genre = g.ToString(), DateCreated = now });
                            inserted++;
                        }
                    }
                }
            }
        }

        _logger.LogInformation("[Database - Seed] genre re-seed complete — {Count} genre rows processed", inserted);
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

    private async Task<ImportBatch> CreateImportBatchAsync(string filePath, string batchLabel)
    {
        var fileName = Path.GetFileName(filePath);
        var batch = new ImportBatch
        {
            Name       = fileName,
            Type       = ImportBatchType.System.ToString(),
            ImportedAt = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat)
        };
        await _importBatches.InsertAsync(batch);
        return batch;
    }

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

    private static string NormaliseType(string raw)
        => ParseQuoteType(raw).ToString();

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

    /// <summary>Loads quote objects from a source file, handling both flat-array and extended-object formats.</summary>
    private static List<SourceQuote> LoadQuotesFromFile(string filePath)
    {
        if (!File.Exists(filePath)) return [];

        var json    = File.ReadAllText(filePath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var root    = JsonNode.Parse(json);

        if (root is JsonArray)
            return JsonSerializer.Deserialize<List<SourceQuote>>(json, options) ?? [];

        // Extended format: root object with a "quotes" array (curated files, import files).
        var quotesNode = root?["quotes"];
        if (quotesNode is null) return [];
        return quotesNode.Deserialize<List<SourceQuote>>(options) ?? [];
    }

    private static string TruncateLabel(string text, int maxLen = 60)
        => text.Length <= maxLen ? text : text[..maxLen] + "…";

    #endregion

    // -------------------------------------------------------------------------
    #region Stats

    private async Task LogDatabaseStatsAsync(SqliteConnection connection)
    {
        QuoteCount     = await connection.ExecuteScalarAsync<int>(Sql.Quotes.CountActive);
        SourceCount    = await connection.ExecuteScalarAsync<int>(Sql.Sources.CountActive);
        CharacterCount = await connection.ExecuteScalarAsync<int>(Sql.Characters.CountActive);
        PeopleCount    = await connection.ExecuteScalarAsync<int>(Sql.People.CountActive);

        _logger.LogInformation(
            "[Database - Stats] {Quotes} quotes  {Sources} sources  {Characters} characters  {People} people",
            QuoteCount, SourceCount, CharacterCount, PeopleCount);
    }

    #endregion

}

using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Dapper.Contrib.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Quotinator.Core.Data.Entities;
using Quotinator.Core.Data.Enums;
using Quotinator.Core.Data.Repositories;
using Quotinator.Core.Helpers;
using Quotinator.Core.Models;
using Quotinator.Data.Connections;
using Quotinator.Data.Models;
using GenreEnum = Quotinator.Core.Data.Enums.Genre;

namespace Quotinator.Core.Data;

/// <summary>
/// Runs schema migrations and seeds the database from one or more source files on first run.
/// Call <see cref="InitialiseAsync"/> once at startup before serving requests.
/// </summary>
public sealed class DatabaseInitializer : IDatabaseInitializer
{
    private readonly IDbConnectionFactory         _factory;
    private readonly string                       _dbPath;
    private readonly string                       _backupsDir;
    private readonly IReadOnlyList<SeedBatch>     _batches;
    private readonly IImportBatchRepository       _importBatches;
    private readonly ILogger<DatabaseInitializer> _logger;

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

    // Numbered migration scripts. Add new entries at the end — never reorder or edit existing ones.
    private static readonly IReadOnlyList<string> Migrations =
    [
        Migration001_InitialSchema,
        Migration002_ReseedGenres,
        Migration003_ImportBatches
    ];

    /// <summary>Initialises the instance with the connection factory and the ordered list of source batches to seed from.</summary>
    /// <param name="factory">Factory used to open SQLite connections.</param>
    /// <param name="dbPath">Absolute path to the <c>.db</c> file. Used for pre-migration backups and legacy filename migration.</param>
    /// <param name="backupsDir">Directory where pre-migration backups are written. Defaults to a <c>backups/</c> subfolder next to the database file.</param>
    /// <param name="batches">Source file batches in import order, each with its resolved duplicate-resolution policy.</param>
    /// <param name="importBatches">Repository used to record provenance for each seeded file.</param>
    /// <param name="logger">Logger for startup diagnostics.</param>
    public DatabaseInitializer(
        IDbConnectionFactory         factory,
        string                       dbPath,
        string                       backupsDir,
        IReadOnlyList<SeedBatch>     batches,
        IImportBatchRepository       importBatches,
        ILogger<DatabaseInitializer> logger)
    {
        _factory       = factory;
        _dbPath        = dbPath;
        _backupsDir    = backupsDir;
        _batches       = batches;
        _importBatches = importBatches;
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
        var dataDir    = Path.GetDirectoryName(_dbPath)!;
        var legacyPath = Path.Combine(dataDir, DataPaths.LegacyDatabaseFile);
        if (!File.Exists(legacyPath) || File.Exists(_dbPath)) return;

        _logger.LogInformation("[Database - Init] migrating legacy filename quotes.db → {NewName}", Path.GetFileName(_dbPath));
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var src = legacyPath + suffix;
            var dst = _dbPath + suffix;
            if (!File.Exists(src)) continue;
            _logger.LogInformation("[Database - Init] moving {Src} → {Dst}", Path.GetFileName(src), Path.GetFileName(dst));
            File.Move(src, dst);
        }
        _logger.LogInformation("[Database - Init] filename migration complete → {Path}", _dbPath);
    }

    private void CreateBackup(SqliteConnection connection, int fromVersion)
    {
        Directory.CreateDirectory(_backupsDir);
        var timestamp  = DateTime.UtcNow.ToString("yyyyMMddTHHmmss");
        var backupName = $"{Path.GetFileNameWithoutExtension(_dbPath)}_v{fromVersion}_{timestamp}Z.db";
        var backupPath = Path.Combine(_backupsDir, backupName);

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

        if (current >= Migrations.Count)
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
                Migrations.Count - current, current, Migrations.Count);
            CreateBackup(connection, current);
        }

        for (var i = current; i < Migrations.Count; i++)
        {
            using var tx = connection.BeginTransaction();
            try
            {
                await connection.ExecuteAsync(Migrations[i], transaction: tx);
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

        SchemaVersion = Migrations.Count;
        if (current > 0)
            MigrationApplied = $"v{current} → v{Migrations.Count}";
        _logger.LogInformation(
            "[Database - Init] schema {Action} at version {Version}",
            current == 0 ? "created" : "updated", Migrations.Count);
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
        SqliteConnection connection, Quote q, Guid quoteId, Guid sourceId, string now)
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

    private async Task InsertGenresAsync(SqliteConnection connection, Quote q, Guid quoteId, string now)
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
        SqliteConnection connection, Quote q, Dictionary<string, Guid> index, Guid importBatchId)
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
        SqliteConnection connection, Quote q, Guid sourceId, Dictionary<string, Guid> index, Guid importBatchId)
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
        SqliteConnection connection, Quote q, Dictionary<string, Guid> index, Guid importBatchId)
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

    private static bool TryNormaliseGenre(string raw, out GenreEnum result)
    {
        if (InputValidation.GenreApiToDb.TryGetValue(raw, out var dbName) &&
            Enum.TryParse<GenreEnum>(dbName, out result))
            return true;
        result = default;
        return false;
    }

    /// <summary>Loads quote objects from a source file, handling both flat-array and extended-object formats.</summary>
    private static List<Quote> LoadQuotesFromFile(string filePath)
    {
        if (!File.Exists(filePath)) return [];

        var json    = File.ReadAllText(filePath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var root    = JsonNode.Parse(json);

        if (root is JsonArray)
            return JsonSerializer.Deserialize<List<Quote>>(json, options) ?? [];

        // Extended format: root object with a "quotes" array (curated files, import files).
        var quotesNode = root?["quotes"];
        if (quotesNode is null) return [];
        return quotesNode.Deserialize<List<Quote>>(options) ?? [];
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

    // -------------------------------------------------------------------------
    #region Schema

    // Clears QuoteGenres so ReSeedGenresIfEmptyAsync can repopulate using the corrected
    // normalisation logic. Hyphenated genres ("sci-fi", "non-fiction") were silently dropped
    // during initial seeding because Enum.TryParse failed on the hyphen.
    private const string Migration002_ReseedGenres = "DELETE FROM QuoteGenres;";

    // Adds the ImportBatches provenance table and nullable ImportBatchId FK columns on all
    // entity tables. Pre-seed rows for the two bundled external datasets are inserted only
    // when upgrading (Quotes already contains data) — fresh installs receive provenance from
    // the seeder instead.
    private const string Migration003_ImportBatches = """
        CREATE TABLE IF NOT EXISTS ImportBatches (
            Id           TEXT    PRIMARY KEY,
            Name         TEXT    NOT NULL,
            Type         TEXT    NOT NULL CHECK (Type IN ('Seed', 'Import', 'System')),
            Url          TEXT,
            ImportedAt   TEXT    NOT NULL,
            ImportedBy   TEXT,
            RecordCount  INTEGER NOT NULL DEFAULT 0,
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0
        );

        ALTER TABLE Quotes     ADD COLUMN ImportBatchId TEXT REFERENCES ImportBatches(Id);
        ALTER TABLE Sources    ADD COLUMN ImportBatchId TEXT REFERENCES ImportBatches(Id);
        ALTER TABLE Characters ADD COLUMN ImportBatchId TEXT REFERENCES ImportBatches(Id);
        ALTER TABLE People     ADD COLUMN ImportBatchId TEXT REFERENCES ImportBatches(Id);

        INSERT INTO ImportBatches (Id, Name, Type, Url, ImportedAt, ImportedBy, RecordCount, DateCreated, DateModified, DateDeleted, IsDeleted)
        SELECT 'A1B2C3D4-E5F6-7890-ABCD-EF1234567890', 'vilaboim_movie-quotes.json', 'Seed',
               'https://github.com/vilaboim/movie-quotes',
               strftime('%Y-%m-%d %H:%M:%S', 'now'), NULL, 0,
               strftime('%Y-%m-%d %H:%M:%S', 'now'), NULL, NULL, 0
        WHERE EXISTS (SELECT 1 FROM Quotes LIMIT 1);

        INSERT INTO ImportBatches (Id, Name, Type, Url, ImportedAt, ImportedBy, RecordCount, DateCreated, DateModified, DateDeleted, IsDeleted)
        SELECT 'B2C3D4E5-F6A7-8901-BCDE-F12345678901', 'NikhilNamal17_popular-movie-quotes.json', 'Seed',
               'https://github.com/NikhilNamal17/popular-movie-quotes',
               strftime('%Y-%m-%d %H:%M:%S', 'now'), NULL, 0,
               strftime('%Y-%m-%d %H:%M:%S', 'now'), NULL, NULL, 0
        WHERE EXISTS (SELECT 1 FROM Quotes LIMIT 1);
        """;

    private const string Migration001_InitialSchema = """
        CREATE TABLE IF NOT EXISTS Sources (
            Id           TEXT    PRIMARY KEY,
            Title        TEXT    NOT NULL,
            Type         TEXT    NOT NULL DEFAULT 'Movie'
                         CHECK (Type IN ('Unknown','Movie','Tv','Anime','Book','Person')),
            Date         TEXT,
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0,
            UNIQUE (Title, Type)
        );

        CREATE TABLE IF NOT EXISTS SourceTranslations (
            Id           TEXT    PRIMARY KEY,
            SourceId     TEXT    NOT NULL REFERENCES Sources(Id),
            Language     TEXT    NOT NULL,
            Title        TEXT    NOT NULL,
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0,
            UNIQUE (SourceId, Language)
        );

        CREATE TABLE IF NOT EXISTS Characters (
            Id           TEXT    PRIMARY KEY,
            SourceId     TEXT    NOT NULL REFERENCES Sources(Id),
            Name         TEXT    NOT NULL,
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0,
            UNIQUE (SourceId, Name)
        );

        CREATE TABLE IF NOT EXISTS CharacterTranslations (
            Id           TEXT    PRIMARY KEY,
            CharacterId  TEXT    NOT NULL REFERENCES Characters(Id),
            Language     TEXT    NOT NULL,
            Name         TEXT    NOT NULL,
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0,
            UNIQUE (CharacterId, Language)
        );

        CREATE TABLE IF NOT EXISTS People (
            Id           TEXT    PRIMARY KEY,
            Name         TEXT    NOT NULL UNIQUE,
            DateOfBirth  TEXT,
            DateOfDeath  TEXT,
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS Quotes (
            Id               TEXT    PRIMARY KEY,
            QuoteText        TEXT    NOT NULL,
            OriginalLanguage TEXT    NOT NULL DEFAULT 'en',
            SourceId         TEXT    NOT NULL REFERENCES Sources(Id),
            CharacterId      TEXT    REFERENCES Characters(Id),
            PersonId         TEXT    REFERENCES People(Id),
            DateCreated      TEXT    NOT NULL,
            DateModified     TEXT,
            DateDeleted      TEXT,
            IsDeleted        INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS QuoteTranslations (
            Id           TEXT    PRIMARY KEY,
            QuoteId      TEXT    NOT NULL REFERENCES Quotes(Id),
            Language     TEXT    NOT NULL,
            QuoteText    TEXT    NOT NULL,
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0,
            UNIQUE (QuoteId, Language)
        );

        CREATE TABLE IF NOT EXISTS QuoteGenres (
            Id           TEXT    PRIMARY KEY,
            QuoteId      TEXT    NOT NULL REFERENCES Quotes(Id),
            Genre        TEXT    NOT NULL
                         CHECK (Genre IN ('Unknown','Action','Adventure','Animation','Comedy','Drama',
                                          'Fantasy','Fiction','Horror','Mystery','NonFiction',
                                          'Romance','SciFi','Thriller')),
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0,
            UNIQUE (QuoteId, Genre)
        );
        """;

    #endregion
}

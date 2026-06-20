using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Dapper.Contrib.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Quotinator.Core.Data.Entities;
using Quotinator.Core.Data.Enums;
using Quotinator.Core.Helpers;
using Quotinator.Core.Models;
using Quotinator.Data.Data;
using Quotinator.Data.Models;
using GenreEnum = Quotinator.Core.Data.Enums.Genre;

namespace Quotinator.Core.Data;

/// <summary>
/// Runs schema migrations and seeds the database from one or more source files on first run.
/// Call <see cref="InitialiseAsync"/> once at startup before serving requests.
/// </summary>
public sealed class DatabaseInitializer : IDatabaseInitializer
{
    private readonly IDbConnectionFactory       _factory;
    private readonly string                     _dbPath;
    private readonly string                     _backupsDir;
    private readonly IReadOnlyList<SeedBatch>   _batches;
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
    public IReadOnlyList<SeedDuplicateRecord> LastSeedDuplicates => _lastSeedDuplicates;

    // Guards against concurrent seeding when multiple WebApplicationFactory instances start in
    // the same process (e.g. parallel MSTest runs). Each waiter re-checks COUNT(*) after
    // acquiring the lock and skips seeding if the previous holder already populated the DB.
    private static readonly SemaphoreSlim _seedLock = new(1, 1);

    // Numbered migration scripts. Add new entries at the end — never reorder or edit existing ones.
    private static readonly IReadOnlyList<string> Migrations =
    [
        Migration001_InitialSchema,
        Migration002_ReseedGenres
    ];

    /// <summary>Initialises the instance with the connection factory and the ordered list of source batches to seed from.</summary>
    /// <param name="factory">Factory used to open SQLite connections.</param>
    /// <param name="dbPath">Absolute path to the <c>.db</c> file. Used for pre-migration backups and legacy filename migration.</param>
    /// <param name="backupsDir">Directory where pre-migration backups are written. Defaults to a <c>backups/</c> subfolder next to the database file.</param>
    /// <param name="batches">Source file batches in import order, each with its resolved duplicate-resolution policy.</param>
    /// <param name="logger">Logger for startup diagnostics.</param>
    public DatabaseInitializer(
        IDbConnectionFactory         factory,
        string                       dbPath,
        string                       backupsDir,
        IReadOnlyList<SeedBatch>     batches,
        ILogger<DatabaseInitializer> logger)
    {
        _factory    = factory;
        _dbPath     = dbPath;
        _backupsDir = backupsDir;
        _batches    = batches;
        _logger     = logger;
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

        _logger.LogInformation("Database: migrating legacy filename quotes.db → {NewName}", Path.GetFileName(_dbPath));
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var src = legacyPath + suffix;
            var dst = _dbPath + suffix;
            if (!File.Exists(src)) continue;
            _logger.LogInformation("Database: moving {Src} → {Dst}", Path.GetFileName(src), Path.GetFileName(dst));
            File.Move(src, dst);
        }
        _logger.LogInformation("Database: filename migration complete → {Path}", _dbPath);
    }

    private void CreateBackup(SqliteConnection connection, int fromVersion)
    {
        Directory.CreateDirectory(_backupsDir);
        var timestamp  = DateTime.UtcNow.ToString("yyyyMMddTHHmmss");
        var backupName = $"{Path.GetFileNameWithoutExtension(_dbPath)}_v{fromVersion}_{timestamp}Z.db";
        var backupPath = Path.Combine(_backupsDir, backupName);

        _logger.LogInformation("Database: backing up v{Version} → {Path}", fromVersion, backupPath);
        using var dest = new SqliteConnection($"Data Source={backupPath}");
        dest.Open();
        connection.BackupDatabase(dest);
        _logger.LogInformation("Database: backup complete");
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
            _logger.LogInformation("Database: schema is up to date at version {Version}", current);
            return;
        }

        if (current == 0)
        {
            _logger.LogInformation("Database: creating schema...");
        }
        else
        {
            _logger.LogInformation(
                "Database: applying {Count} pending migration(s) (version {Current} → {Target})...",
                Migrations.Count - current, current, Migrations.Count);
            CreateBackup(connection, current);
        }

        for (var i = current; i < Migrations.Count; i++)
        {
            await connection.ExecuteAsync(Migrations[i]);
            await connection.ExecuteAsync(
                Sql.Schema.InsertVersion,
                new { v = i + 1, at = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat) });
        }

        SchemaVersion = Migrations.Count;
        _logger.LogInformation(
            "Database: schema {Action} at version {Version}",
            current == 0 ? "created" : "updated", Migrations.Count);
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
        _logger.LogInformation("Database: reseed requested — clearing all data and reimporting from {Count} source file(s)...", totalFiles);

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
        _logger.LogInformation("Database: reseed complete");
    }

    /// <inheritdoc/>
    public async Task ResetAsync()
    {
        using var connection = (SqliteConnection)_factory.CreateConnection();
        await connection.OpenAsync();

        var totalFiles = _batches.Sum(b => b.Files.Count);
        _logger.LogInformation("Database: reset requested — rebuilding schema and reimporting from {Count} source file(s)...", totalFiles);

        await _seedLock.WaitAsync();
        try
        {
            await TruncateDataAsync(connection);
            await connection.ExecuteAsync(Sql.Schema.DeleteAll);
            await ApplyMigrationsAsync(connection);
            await SeedIfEmptyInternalAsync(connection);
        }
        finally
        {
            _seedLock.Release();
        }

        await LogDatabaseStatsAsync(connection);
        _logger.LogInformation("Database: reset complete");
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
        await connection.ExecuteAsync("PRAGMA foreign_keys = ON;");
    }

    private async Task SeedIfEmptyInternalAsync(SqliteConnection connection)
    {
        var count = await connection.ExecuteScalarAsync<int>(Sql.Quotes.CountAll);
        if (count > 0) return;

        if (_batches.Count == 0)
        {
            _logger.LogWarning("Database: no source files configured — database will be empty");
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
                var fileName = Path.GetFileName(file);
                var quotes   = LoadQuotesFromFile(file);
                _logger.LogInformation("Database: importing {Count} quotes from {File} ({Batch})...",
                    quotes.Count, fileName, batch.Label);

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
                                "Database: skipping duplicate quote {Id} in {File} (first seen in {First})",
                                q.Id, fileName, Path.GetFileName(firstFile));
                            continue;
                        }

                        // OVERWRITE: delete children first (FK constraint), then update the parent.
                        _logger.LogDebug(
                            "Database: overwriting duplicate quote {Id} in {File} (was {First})",
                            q.Id, fileName, Path.GetFileName(firstFile));

                        await connection.ExecuteAsync(Sql.QuoteGenres.DeleteForQuote,      new { id = q.Id });
                        await connection.ExecuteAsync(Sql.QuoteTranslations.DeleteForQuote, new { id = q.Id });

                        var owSourceId    = await GetOrCreateSourceAsync(connection, q, sourceIndex);
                        var owCharacterId = await GetOrCreateCharacterAsync(connection, q, owSourceId, characterIndex);
                        var owPersonId    = await GetOrCreatePersonAsync(connection, q, personIndex);

                        await connection.ExecuteAsync(
                            Sql.Quotes.UpdateOnOverwrite,
                            new
                            {
                                text = q.QuoteText,
                                lang = q.OriginalLanguage,
                                sid  = owSourceId,
                                cid  = owCharacterId,
                                pid  = owPersonId,
                                mod  = now,
                                id   = q.Id
                            });

                        seenIds[q.Id] = file;

                        var owQuoteId = Guid.Parse(q.Id);
                        await InsertTranslationsAsync(connection, q, owQuoteId, owSourceId, now);
                        await InsertGenresAsync(connection, q, owQuoteId, now);
                        continue;
                    }

                    // First occurrence — normal insert.
                    seenIds[q.Id] = file;

                    var sourceId    = await GetOrCreateSourceAsync(connection, q, sourceIndex);
                    var characterId = await GetOrCreateCharacterAsync(connection, q, sourceId, characterIndex);
                    var personId    = await GetOrCreatePersonAsync(connection, q, personIndex);
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
                            DateCreated      = now
                        });

                    await InsertTranslationsAsync(connection, q, quoteId, sourceId, now);
                    await InsertGenresAsync(connection, q, quoteId, now);
                }
            }
        }

        var dupCount = _lastSeedDuplicates.Count;
        _logger.LogInformation(
            "Database: seeding complete — {Unique} unique quotes from {Total} total ({Dups} duplicate{S})",
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
            _logger.LogWarning("Database: cannot re-seed genres — no source files configured");
            return;
        }

        _logger.LogInformation("Database: re-seeding genres from source files...");

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

        _logger.LogInformation("Database: genre re-seed complete — {Count} genre rows processed", inserted);
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

    private static async Task<Guid> GetOrCreateSourceAsync(
        SqliteConnection connection, Quote q, Dictionary<string, Guid> index)
    {
        var typeStr = NormaliseType(q.Type);
        var key     = $"{q.Source}|{typeStr}";
        if (index.TryGetValue(key, out var existing)) return existing;

        var id = Guid.NewGuid();
        await connection.InsertAsync(new Source
        {
            Id    = id,
            Title = q.Source,
            Type  = new SafeValue<QuoteType?>(typeStr, ParseQuoteType(q.Type)),
            Date  = string.IsNullOrEmpty(q.Date) ? SafeDateValue.Empty : new SafeValue<DateTime?>(q.Date, null)
        });

        index[key] = id;
        return id;
    }

    private static async Task<Guid?> GetOrCreateCharacterAsync(
        SqliteConnection connection, Quote q, Guid sourceId, Dictionary<string, Guid> index)
    {
        if (string.IsNullOrWhiteSpace(q.Character)) return null;

        var key = $"{sourceId}|{q.Character}";
        if (index.TryGetValue(key, out var existing)) return existing;

        var id = Guid.NewGuid();
        await connection.InsertAsync(new Character
        {
            Id       = id,
            SourceId = sourceId,
            Name     = q.Character
        });

        index[key] = id;
        return id;
    }

    private static async Task<Guid?> GetOrCreatePersonAsync(
        SqliteConnection connection, Quote q, Dictionary<string, Guid> index)
    {
        if (string.IsNullOrWhiteSpace(q.Author)) return null;

        if (index.TryGetValue(q.Author, out var existing)) return existing;

        var id = Guid.NewGuid();
        await connection.InsertAsync(new Person
        {
            Id   = id,
            Name = q.Author
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
            "Database ready — {Quotes} quotes, {Sources} sources, {Characters} characters, {People} people",
            QuoteCount, SourceCount, CharacterCount, PeopleCount);
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Schema

    // Clears QuoteGenres so ReSeedGenresIfEmptyAsync can repopulate using the corrected
    // normalisation logic. Hyphenated genres ("sci-fi", "non-fiction") were silently dropped
    // during initial seeding because Enum.TryParse failed on the hyphen.
    private const string Migration002_ReseedGenres = "DELETE FROM QuoteGenres;";

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

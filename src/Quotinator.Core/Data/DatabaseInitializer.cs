using System.Text.Json;
using Dapper;
using Dapper.Contrib.Extensions;
using Microsoft.Data.Sqlite;
using Quotinator.Core.Data.Entities;
using Quotinator.Core.Data.Enums;
using Quotinator.Core.Models;
using GenreEnum = Quotinator.Core.Data.Enums.Genre;

namespace Quotinator.Core.Data;

/// <summary>
/// Runs schema migrations and seeds the database from quotes.json on first run.
/// Call <see cref="InitialiseAsync"/> once at startup before serving requests.
/// </summary>
public sealed class DatabaseInitializer
{
    private readonly IDbConnectionFactory _factory;
    private readonly string _seedJsonPath;

    // Numbered migration scripts. Add new entries at the end — never reorder or edit existing ones.
    private static readonly IReadOnlyList<string> Migrations =
    [
        Migration001_InitialSchema
    ];

    public DatabaseInitializer(IDbConnectionFactory factory, string seedJsonPath)
    {
        _factory  = factory;
        _seedJsonPath = seedJsonPath;
    }

    public async Task InitialiseAsync()
    {
        using var connection = (SqliteConnection)_factory.CreateConnection();
        await connection.OpenAsync();

        EnableWal(connection);
        await ApplyMigrationsAsync(connection);
        await SeedIfEmptyAsync(connection);
    }

    // -------------------------------------------------------------------------
    #region Migrations

    private static void EnableWal(SqliteConnection connection)
        => connection.Execute("PRAGMA journal_mode=WAL;");

    private static async Task ApplyMigrationsAsync(SqliteConnection connection)
    {
        await connection.ExecuteAsync(
            "CREATE TABLE IF NOT EXISTS SchemaVersion (Version INTEGER NOT NULL, AppliedAt TEXT NOT NULL);");

        var current = await connection.ExecuteScalarAsync<int>(
            "SELECT COALESCE(MAX(Version), 0) FROM SchemaVersion;");

        for (var i = current; i < Migrations.Count; i++)
        {
            await connection.ExecuteAsync(Migrations[i]);
            await connection.ExecuteAsync(
                "INSERT INTO SchemaVersion (Version, AppliedAt) VALUES (@v, @at);",
                new { v = i + 1, at = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat) });
        }
    }

    #endregion

    // -------------------------------------------------------------------------
    #region Seeding

    private async Task SeedIfEmptyAsync(SqliteConnection connection)
    {
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Quotes;");
        if (count > 0) return;

        if (!File.Exists(_seedJsonPath)) return;

        var json    = await File.ReadAllTextAsync(_seedJsonPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var quotes  = JsonSerializer.Deserialize<List<Quote>>(json, options) ?? [];

        // Index lookups to avoid duplicate inserts within this seeding run.
        var sourceIndex    = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var characterIndex = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var personIndex    = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        foreach (var q in quotes)
        {
            var sourceId    = await GetOrCreateSourceAsync(connection, q, sourceIndex);
            var characterId = await GetOrCreateCharacterAsync(connection, q, sourceId, characterIndex);
            var personId    = await GetOrCreatePersonAsync(connection, q, personIndex);

            var quoteId = Guid.Parse(q.Id);
            await connection.InsertAsync(new QuoteEntity
            {
                Id               = quoteId,
                QuoteText        = q.QuoteText,
                OriginalLanguage = q.OriginalLanguage,
                SourceId         = sourceId,
                CharacterId      = characterId,
                PersonId         = personId
            });

            foreach (var (lang, t) in q.Translations)
            {
                await connection.InsertAsync(new QuoteTranslationEntity
                {
                    QuoteId   = quoteId,
                    Language  = lang,
                    QuoteText = t.QuoteText
                });

                if (t.Source is not null)
                {
                    // Upsert the translated source title if we haven't stored it yet.
                    var exists = await connection.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM SourceTranslations WHERE SourceId = @sid AND Language = @lang AND IsDeleted = 0;",
                        new { sid = sourceId.ToString(), lang });
                    if (exists == 0)
                        await connection.InsertAsync(new SourceTranslation
                        {
                            SourceId = sourceId,
                            Language = lang,
                            Title    = t.Source
                        });
                }
            }

            foreach (var genre in q.Genres)
            {
                if (Enum.TryParse<GenreEnum>(genre, ignoreCase: true, out var g))
                {
                    await connection.InsertAsync(new QuoteGenreEntity
                    {
                        QuoteId = quoteId,
                        Genre   = new SafeValue<GenreEnum?>(g.ToString(), g)
                    });
                }
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
            Id   = id,
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

    #endregion

    // -------------------------------------------------------------------------
    #region Schema

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

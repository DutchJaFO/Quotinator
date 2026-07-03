using Quotinator.Data.Database;

namespace Quotinator.Engine.Database;

/// <summary>
/// Ordered, append-only list of Quotinator's own domain schema migrations — applied after
/// Quotinator.Data's own migrations (see <c>DatabaseInitializer.DataOwnedMigrations</c>), tracked
/// in their own <c>System_ConsumerSchemaVersion</c> table, independent of Quotinator.Data's version
/// counter. Passed to <see cref="QuotinatorDatabaseInitializer"/> at startup via DI.
/// </summary>
/// <remarks>
/// Never reorder or edit an existing entry. Every SQL statement must be idempotent.
/// Add new migrations at the end and increment the version by one.
/// </remarks>
public static class QuotinatorMigrations
{
    /// <summary>Quotinator's own domain schema migrations, in application order.</summary>
    public static IReadOnlyList<SchemaMigration> All { get; } =
    [
        new SchemaMigration { Version = 1, Sql = Migration001_InitialSchema },
        new SchemaMigration { Version = 2, Sql = Migration002_ReseedGenres },
        new SchemaMigration { Version = 3, Sql = Migration003_ImportBatches },
        new SchemaMigration { Version = 4, Sql = Migration004_ImportBatchTypeUserSeed },
    ];

    /// <summary>
    /// Consolidated DDL that creates Quotinator's own domain schema directly at its current
    /// version, used only for a genuinely fresh database. Quotinator.Data's own tables (e.g.
    /// <c>System_AuditEntries</c>) are created separately and are not this baseline's concern.
    /// </summary>
    public static SchemaBaseline Baseline { get; } = new SchemaBaseline { Sql = BaselineSchema };

    // All tables use RecordBase columns (Id, DateCreated, DateModified, DateDeleted, IsDeleted).
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

    // Widens the ImportBatches.Type CHECK constraint to add 'UserSeed' (files scanned from the
    // user's imports folder, distinct from 'System'/'Seed' bundled content). SQLite cannot ALTER
    // a CHECK constraint, so the table is recreated with the new constraint and existing rows are
    // copied across. Wrapped in the caller's transaction, so a failure rolls back to the
    // pre-migration table intact — safe to retry.
    private const string Migration004_ImportBatchTypeUserSeed = """
        CREATE TABLE IF NOT EXISTS ImportBatches_New (
            Id           TEXT    PRIMARY KEY,
            Name         TEXT    NOT NULL,
            Type         TEXT    NOT NULL CHECK (Type IN ('Seed', 'Import', 'System', 'UserSeed')),
            Url          TEXT,
            ImportedAt   TEXT    NOT NULL,
            ImportedBy   TEXT,
            RecordCount  INTEGER NOT NULL DEFAULT 0,
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0
        );

        INSERT INTO ImportBatches_New (Id, Name, Type, Url, ImportedAt, ImportedBy, RecordCount, DateCreated, DateModified, DateDeleted, IsDeleted)
        SELECT Id, Name, Type, Url, ImportedAt, ImportedBy, RecordCount, DateCreated, DateModified, DateDeleted, IsDeleted
        FROM ImportBatches;

        DROP TABLE ImportBatches;

        ALTER TABLE ImportBatches_New RENAME TO ImportBatches;
        """;

    // Consolidated schema for a genuinely fresh database — the union of migrations 1-4's final
    // result, with ImportBatchId baked directly into the four entity tables (migration003's
    // ALTER TABLE ADD COLUMN always appends, so it's listed last here to match column order) and
    // ImportBatches using the final widened CHECK constraint (migration004). Deliberately omits
    // migration002's DELETE FROM QuoteGenres (data-repair for pre-existing bad data — nothing to
    // repair on a fresh database) and migration003's pre-seed INSERTs (WHERE EXISTS-guarded,
    // always a no-op before any quote has been seeded). Kept in sync with migrations 1-4 by
    // QuotinatorMigrationsBaselineTests' schema-drift comparison.
    private const string BaselineSchema = """
        CREATE TABLE IF NOT EXISTS ImportBatches (
            Id           TEXT    PRIMARY KEY,
            Name         TEXT    NOT NULL,
            Type         TEXT    NOT NULL CHECK (Type IN ('Seed', 'Import', 'System', 'UserSeed')),
            Url          TEXT,
            ImportedAt   TEXT    NOT NULL,
            ImportedBy   TEXT,
            RecordCount  INTEGER NOT NULL DEFAULT 0,
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0
        );

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
            ImportBatchId TEXT   REFERENCES ImportBatches(Id),
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
            ImportBatchId TEXT   REFERENCES ImportBatches(Id),
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
            IsDeleted    INTEGER NOT NULL DEFAULT 0,
            ImportBatchId TEXT   REFERENCES ImportBatches(Id)
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
            IsDeleted        INTEGER NOT NULL DEFAULT 0,
            ImportBatchId    TEXT    REFERENCES ImportBatches(Id)
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
}

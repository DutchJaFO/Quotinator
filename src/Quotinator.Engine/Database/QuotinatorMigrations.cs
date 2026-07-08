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
        new SchemaMigration { Version = 5, Sql = Migration005_ImportBatchConflictPolicy },
        new SchemaMigration { Version = 6, Sql = Migration006_RecordCompleteness },
        new SchemaMigration { Version = 7, Sql = Migration007_ImportBatchStagingStatus },
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

    // Adds ImportBatches.ConflictPolicy, recording the conflict-resolution policy that was active
    // for each batch. Pre-existing rows backfill to 'skip' — the HardcodedDefault in effect before
    // #64 flipped it to NewestWins — since that's what those rows were actually seeded under; new
    // rows populate their real applied policy at insert time via CreateImportBatchAsync.
    private const string Migration005_ImportBatchConflictPolicy = """
        ALTER TABLE ImportBatches ADD COLUMN ConflictPolicy TEXT NOT NULL DEFAULT 'skip';
        """;

    // Adds IsComplete (human-set "reviewed and satisfied" flag) and NoValueKnown (JSON array of
    // field names confirmed to have no findable value) to all four entity tables, per #55. Both
    // columns default false/'[]' for pre-existing rows on upgrade — correct, since no row predating
    // this migration has ever been reviewed. #64's UPDATE paths (Sql.Quotes.UpdateOnNewestWins and
    // the GetOrCreate* "found existing" paths) deliberately never reference these columns, so an
    // existing row's values survive every reseed/reimport untouched.
    private const string Migration006_RecordCompleteness = """
        ALTER TABLE Quotes     ADD COLUMN IsComplete BIT NOT NULL DEFAULT 0;
        ALTER TABLE Quotes     ADD COLUMN NoValueKnown TEXT NOT NULL DEFAULT '[]';
        ALTER TABLE Sources    ADD COLUMN IsComplete BIT NOT NULL DEFAULT 0;
        ALTER TABLE Sources    ADD COLUMN NoValueKnown TEXT NOT NULL DEFAULT '[]';
        ALTER TABLE Characters ADD COLUMN IsComplete BIT NOT NULL DEFAULT 0;
        ALTER TABLE Characters ADD COLUMN NoValueKnown TEXT NOT NULL DEFAULT '[]';
        ALTER TABLE People     ADD COLUMN IsComplete BIT NOT NULL DEFAULT 0;
        ALTER TABLE People     ADD COLUMN NoValueKnown TEXT NOT NULL DEFAULT '[]';
        """;

    // Adds ImportBatches.Status ('Staged'/'Applied'/'Discarded') and AppliedAt (#154). Every
    // pre-existing code path (live import, preview, seeding) always committed immediately, so
    // pre-existing rows correctly backfill to 'Applied' — nothing before this feature ever staged.
    // Only the new staging endpoint creates 'Staged' rows. Status is backed by a real C# enum
    // (ImportBatchStatus), so it gets a CHECK constraint per ADR 008 (enum-backed columns require
    // a matching CHECK). Confirmed against sqlite.org's ALTER TABLE docs, not just empirical testing
    // — ADD COLUMN explicitly supports a CHECK constraint (existing rows are tested against it),
    // and this column satisfies every documented restriction (no PRIMARY KEY/UNIQUE, NOT NULL has a
    // real default, no REFERENCES clause, not a GENERATED STORED column).
    private const string Migration007_ImportBatchStagingStatus = """
        ALTER TABLE ImportBatches ADD COLUMN Status TEXT NOT NULL DEFAULT 'Applied'
            CHECK (Status IN ('Staged', 'Applied', 'Discarded'));
        ALTER TABLE ImportBatches ADD COLUMN AppliedAt TEXT;
        """;

    // Consolidated schema for a genuinely fresh database — the union of migrations 1-7's final
    // result, with ImportBatchId baked directly into the four entity tables (migration003's
    // ALTER TABLE ADD COLUMN always appends, so it's listed last here to match column order),
    // ImportBatches using the final widened CHECK constraint (migration004), ImportBatches.
    // ConflictPolicy (migration005's ALTER TABLE ADD COLUMN, also always appends, so it's listed
    // last too) present with the same 'skip' default backfill value, IsComplete/NoValueKnown
    // (migration006's ALTER TABLE ADD COLUMN, appended last again) on the four entity tables, and
    // ImportBatches.Status/AppliedAt (migration007's ALTER TABLE ADD COLUMN, appended last) with the
    // same 'Applied' default backfill value.
    // Deliberately omits migration002's DELETE FROM QuoteGenres (data-repair for pre-existing bad
    // data — nothing to repair on a fresh database) and migration003's pre-seed INSERTs (WHERE
    // EXISTS-guarded, always a no-op before any quote has been seeded). Kept in sync with
    // migrations 1-7 by DatabaseInitializerTests' schema-drift comparison.
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
            IsDeleted    INTEGER NOT NULL DEFAULT 0,
            ConflictPolicy TEXT  NOT NULL DEFAULT 'skip',
            Status       TEXT    NOT NULL DEFAULT 'Applied'
                         CHECK (Status IN ('Staged', 'Applied', 'Discarded')),
            AppliedAt    TEXT
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
            IsComplete   BIT     NOT NULL DEFAULT 0,
            NoValueKnown TEXT    NOT NULL DEFAULT '[]',
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
            IsComplete   BIT     NOT NULL DEFAULT 0,
            NoValueKnown TEXT    NOT NULL DEFAULT '[]',
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
            ImportBatchId TEXT   REFERENCES ImportBatches(Id),
            IsComplete   BIT     NOT NULL DEFAULT 0,
            NoValueKnown TEXT    NOT NULL DEFAULT '[]'
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
            ImportBatchId    TEXT    REFERENCES ImportBatches(Id),
            IsComplete       BIT     NOT NULL DEFAULT 0,
            NoValueKnown     TEXT    NOT NULL DEFAULT '[]'
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

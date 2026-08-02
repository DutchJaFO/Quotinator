using Quotinator.Data.Database;

namespace Quotinator.Core.Database;

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
        new SchemaMigration { Version = 4, Sql = Migration004_ConsolidatedSinceV172 },
        new SchemaMigration { Version = 5, Sql = Migration005_ImportBatchConflictPolicyCheckConstraint },
        new SchemaMigration { Version = 6, Sql = Migration006_DomainPrefixRename },
    ];

    /// <summary>
    /// Consolidated DDL that creates Quotinator's own domain schema directly at its current
    /// version, used only for a genuinely fresh database. Quotinator.Data's own tables (e.g.
    /// <c>System_AuditEntries</c>) are created separately and are not this baseline's concern.
    /// </summary>
    public static SchemaBaseline Baseline { get; } = new SchemaBaseline { Sql = BaselineSchema };

    // All tables use RecordBase columns (Id, DateCreated, DateModified, DateDeleted, IsDeleted).
    // internal (not private): frozen forever (already released in v1.7.2, confirmed against `main`
    // — never editable per this project's migration policy), so tests may reference this constant
    // directly to build a genuine v1.7.2-shaped fixture database without ever truncating the real
    // migration list passed to a DatabaseInitializer (see #155's "we do not skip migrations for any
    // reason" — a test must always run the real, complete migration path; it may only construct
    // realistic pre-existing data for that real path to run against).
    internal const string Migration001_InitialSchema = """
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
    internal const string Migration002_ReseedGenres = "DELETE FROM QuoteGenres;";

    // Adds the ImportBatches provenance table and nullable ImportBatchId FK columns on all
    // entity tables. Pre-seed rows for the two bundled external datasets are inserted only
    // when upgrading (Quotes already contains data) — fresh installs receive provenance from
    // the seeder instead.
    internal const string Migration003_ImportBatches = """
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

    // #155: consolidates every Consumer-owned migration added since v1.7.2 (the last actually
    // published release — confirmed directly against the `main` branch, which still has only
    // migrations 1-3 here) into a single migration. None of the 8 migrations this replaces
    // (ImportBatchTypeUserSeed, ImportBatchConflictPolicy, RecordCompleteness,
    // ImportBatchStagingStatus, Conversations, SeriesUniverseSchema,
    // RenameImportBatchImportedById, CharacterGlobalIdentity) had ever reached a real installation,
    // so squashing them is safe under this project's migration policy (which protects migrations
    // that have shipped, not ones still in development) — see the #155 plan doc for the full
    // reasoning and the developer's explicit direction to consolidate. Combining previously-separate
    // transactions into one is strictly safer (fully atomic), never less so. Content below is the
    // literal concatenation of the 8 migrations' SQL bodies, in their original order — no logic
    // changed, only where the version boundaries used to fall. Per-step reasoning preserved from
    // each original migration's own doc comment:
    //
    // ImportBatchTypeUserSeed: widens ImportBatches.Type's CHECK to add 'UserSeed' (imports-folder
    // files, distinct from bundled 'System'/'Seed' content) via the rebuild-under-temporary-name
    // pattern (SQLite cannot ALTER a CHECK constraint in place).
    //
    // ImportBatchConflictPolicy: adds ImportBatches.ConflictPolicy, recording which
    // duplicate-resolution policy was active per batch; pre-existing rows backfill to 'skip' (the
    // HardcodedDefault in effect before #64 flipped it to NewestWins).
    //
    // RecordCompleteness: adds CompletenessStatus (Incomplete/NeedsReview/Complete, #55/#165) and
    // NoValueKnown (confirmed-empty field list) to Quotes/Sources/Characters/People, enum-backed so
    // CompletenessStatus gets a CHECK per ADR 008; both default to the "never reviewed" state for
    // pre-existing rows.
    //
    // ImportBatchStagingStatus: adds ImportBatches.Status ('Staged'/'Applied'/'Discarded', #154) and
    // AppliedAt; every pre-#154 code path committed immediately, so pre-existing rows backfill to
    // 'Applied'.
    //
    // Conversations: adds Conversations/ConversationLines/StageDirections/StageDirectionTranslations/
    // SoundCues/SoundCueTranslations (#67), every table with full RecordBase columns (ADR 002).
    // ConversationLines.LineType and the cross-field "exactly one FK matches LineType" rule are two
    // independent CHECKs per ADR 008. Conversations/StageDirections/SoundCues get
    // CompletenessStatus/NoValueKnown inline since they're created fresh here.
    //
    // SeriesUniverseSchema (#179/ADR 011): adds the Universe -> Series -> Source hierarchy, and
    // makes Character<->Source many-to-many via CharacterSources (replacing Characters.SourceId's
    // single required FK) with zero data merging — every existing Character (including soft-deleted
    // ones) gets exactly one CharacterSources row carrying its then-current SourceId before the
    // column is dropped. CharacterSources.Id is generated via randomblob()/hex() (SQLite has no
    // native UUID function), which always produces uppercase hex — matching this project's canonical
    // convention at the time this was written; ADR 012 later moved the convention to lowercase, but
    // since nothing outside this migration ever looks a CharacterSources row up by its own Id (only
    // by the CharacterId/SourceId pair via IdClauses), the mismatch is inert and permanent by design.
    // Characters.SourceId and its UNIQUE(SourceId, Name) constraint are dropped via the same
    // rebuild-under-temporary-name pattern ImportBatchTypeUserSeed above uses.
    //
    // RenameImportBatchImportedById (#213): renames ImportBatches.ImportedBy to ImportedById so the
    // column carries the *Id suffix every id-casing guard relies on to find id/FK columns by name —
    // a single atomic RENAME COLUMN, no CHECK/UNIQUE/REFERENCES on this column to rebuild around.
    //
    // CharacterGlobalIdentity (#174, ADR 013): consolidates pre-existing per-Source Character rows
    // into fewer global rows using the merge-candidate test ADR 013 Decision 1 defines — same Name
    // (case-insensitive), same denormalized SourceType (backfilled first, from each row's single
    // linked Source — every Character still has exactly one CharacterSources link at this exact
    // point in migration history, since SeriesUniverseSchema above performed zero merging), and a
    // directly shared, non-null Sources.SeriesId between the two Characters' linked Sources. A
    // Character with no Series-known linked Source is conservatively excluded from merging
    // (Decision 1(c)). CharacterSources/Quotes.CharacterId are re-pointed to each group's survivor,
    // CompletenessStatus resolves to the most-reviewed value across the group (Decision 4),
    // merged-away rows are soft-deleted, never hard-deleted (Decision 3).
    // Split from CharacterGlobalIdentityMerge (below) purely so tests can run this schema-creation
    // portion standalone, populate Sources.SeriesId by hand (nothing in this migration itself ever
    // seeds SeriesId values — that only ever happens via the app's own later import/seeding path),
    // then run the merge portion separately against realistic data — see
    // DatabaseInitializerTests' Migration_CharacterGlobalIdentity_* tests. Not meant to represent a
    // real, independently-reachable migration checkpoint on its own.
    internal const string Migration004_ConsolidatedSinceV172Core = """
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

        ALTER TABLE ImportBatches ADD COLUMN ConflictPolicy TEXT NOT NULL DEFAULT 'skip';

        ALTER TABLE Quotes     ADD COLUMN CompletenessStatus TEXT NOT NULL DEFAULT 'Incomplete'
            CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview', 'Complete'));
        ALTER TABLE Quotes     ADD COLUMN NoValueKnown TEXT NOT NULL DEFAULT '[]';
        ALTER TABLE Sources    ADD COLUMN CompletenessStatus TEXT NOT NULL DEFAULT 'Incomplete'
            CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview', 'Complete'));
        ALTER TABLE Sources    ADD COLUMN NoValueKnown TEXT NOT NULL DEFAULT '[]';
        ALTER TABLE Characters ADD COLUMN CompletenessStatus TEXT NOT NULL DEFAULT 'Incomplete'
            CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview', 'Complete'));
        ALTER TABLE Characters ADD COLUMN NoValueKnown TEXT NOT NULL DEFAULT '[]';
        ALTER TABLE People     ADD COLUMN CompletenessStatus TEXT NOT NULL DEFAULT 'Incomplete'
            CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview', 'Complete'));
        ALTER TABLE People     ADD COLUMN NoValueKnown TEXT NOT NULL DEFAULT '[]';

        ALTER TABLE ImportBatches ADD COLUMN Status TEXT NOT NULL DEFAULT 'Applied'
            CHECK (Status IN ('Staged', 'Applied', 'Discarded'));
        ALTER TABLE ImportBatches ADD COLUMN AppliedAt TEXT;

        CREATE TABLE IF NOT EXISTS Conversations (
            Id                 TEXT    PRIMARY KEY,
            Description        TEXT,
            ImportBatchId      TEXT    REFERENCES ImportBatches(Id),
            CompletenessStatus TEXT    NOT NULL DEFAULT 'Incomplete'
                               CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview', 'Complete')),
            NoValueKnown       TEXT    NOT NULL DEFAULT '[]',
            DateCreated        TEXT    NOT NULL,
            DateModified       TEXT,
            DateDeleted        TEXT,
            IsDeleted          INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS StageDirections (
            Id                 TEXT    PRIMARY KEY,
            Text               TEXT    NOT NULL,
            ImageUrl           TEXT,
            ImportBatchId      TEXT    REFERENCES ImportBatches(Id),
            CompletenessStatus TEXT    NOT NULL DEFAULT 'Incomplete'
                               CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview', 'Complete')),
            NoValueKnown       TEXT    NOT NULL DEFAULT '[]',
            DateCreated        TEXT    NOT NULL,
            DateModified       TEXT,
            DateDeleted        TEXT,
            IsDeleted          INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS StageDirectionTranslations (
            Id               TEXT    PRIMARY KEY,
            StageDirectionId TEXT    NOT NULL REFERENCES StageDirections(Id),
            Language         TEXT    NOT NULL,
            Text             TEXT    NOT NULL,
            DateCreated      TEXT    NOT NULL,
            DateModified     TEXT,
            DateDeleted      TEXT,
            IsDeleted        INTEGER NOT NULL DEFAULT 0,
            UNIQUE (StageDirectionId, Language)
        );

        CREATE TABLE IF NOT EXISTS SoundCues (
            Id                 TEXT    PRIMARY KEY,
            Text               TEXT    NOT NULL,
            SoundFileUrl       TEXT,
            ImageUrl           TEXT,
            ImportBatchId      TEXT    REFERENCES ImportBatches(Id),
            CompletenessStatus TEXT    NOT NULL DEFAULT 'Incomplete'
                               CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview', 'Complete')),
            NoValueKnown       TEXT    NOT NULL DEFAULT '[]',
            DateCreated        TEXT    NOT NULL,
            DateModified       TEXT,
            DateDeleted        TEXT,
            IsDeleted          INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS SoundCueTranslations (
            Id           TEXT    PRIMARY KEY,
            SoundCueId   TEXT    NOT NULL REFERENCES SoundCues(Id),
            Language     TEXT    NOT NULL,
            Text         TEXT    NOT NULL,
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0,
            UNIQUE (SoundCueId, Language)
        );

        CREATE TABLE IF NOT EXISTS ConversationLines (
            Id                TEXT    PRIMARY KEY,
            ConversationId    TEXT    NOT NULL REFERENCES Conversations(Id),
            [Order]           INTEGER NOT NULL,
            LineType          TEXT    NOT NULL
                              CHECK (LineType IN ('Quote','StageDirection','SoundCue')),
            QuoteId           TEXT    REFERENCES Quotes(Id),
            StageDirectionId  TEXT    REFERENCES StageDirections(Id),
            SoundCueId        TEXT    REFERENCES SoundCues(Id),
            DateCreated       TEXT    NOT NULL,
            DateModified      TEXT,
            DateDeleted       TEXT,
            IsDeleted         INTEGER NOT NULL DEFAULT 0,
            CHECK (
                (LineType = 'Quote'          AND QuoteId          IS NOT NULL AND StageDirectionId IS NULL AND SoundCueId IS NULL) OR
                (LineType = 'StageDirection' AND StageDirectionId IS NOT NULL AND QuoteId          IS NULL AND SoundCueId IS NULL) OR
                (LineType = 'SoundCue'       AND SoundCueId       IS NOT NULL AND QuoteId          IS NULL AND StageDirectionId IS NULL)
            ),
            UNIQUE (ConversationId, [Order])
        );

        CREATE INDEX IF NOT EXISTS IX_ConversationLines_ConversationId           ON ConversationLines(ConversationId);
        CREATE INDEX IF NOT EXISTS IX_ConversationLines_QuoteId                  ON ConversationLines(QuoteId);
        CREATE INDEX IF NOT EXISTS IX_ConversationLines_StageDirectionId         ON ConversationLines(StageDirectionId);
        CREATE INDEX IF NOT EXISTS IX_ConversationLines_SoundCueId               ON ConversationLines(SoundCueId);
        CREATE INDEX IF NOT EXISTS IX_StageDirectionTranslations_StageDirectionId ON StageDirectionTranslations(StageDirectionId);
        CREATE INDEX IF NOT EXISTS IX_SoundCueTranslations_SoundCueId            ON SoundCueTranslations(SoundCueId);

        CREATE TABLE IF NOT EXISTS Universe (
            Id                 TEXT    PRIMARY KEY,
            Name               TEXT    NOT NULL UNIQUE,
            ImportBatchId      TEXT    REFERENCES ImportBatches(Id),
            CompletenessStatus TEXT    NOT NULL DEFAULT 'Incomplete'
                               CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview', 'Complete')),
            NoValueKnown       TEXT    NOT NULL DEFAULT '[]',
            DateCreated        TEXT    NOT NULL,
            DateModified       TEXT,
            DateDeleted        TEXT,
            IsDeleted          INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS Series (
            Id                 TEXT    PRIMARY KEY,
            Name               TEXT    NOT NULL UNIQUE,
            UniverseId         TEXT    REFERENCES Universe(Id),
            ImportBatchId      TEXT    REFERENCES ImportBatches(Id),
            CompletenessStatus TEXT    NOT NULL DEFAULT 'Incomplete'
                               CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview', 'Complete')),
            NoValueKnown       TEXT    NOT NULL DEFAULT '[]',
            DateCreated        TEXT    NOT NULL,
            DateModified       TEXT,
            DateDeleted        TEXT,
            IsDeleted          INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS CharacterSources (
            Id           TEXT    PRIMARY KEY,
            CharacterId  TEXT    NOT NULL REFERENCES Characters(Id),
            SourceId     TEXT    NOT NULL REFERENCES Sources(Id),
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0,
            UNIQUE (CharacterId, SourceId)
        );

        INSERT INTO CharacterSources (Id, CharacterId, SourceId, DateCreated, IsDeleted)
        SELECT
            upper(hex(randomblob(4))) || '-' || upper(hex(randomblob(2))) || '-' ||
            upper(hex(randomblob(2))) || '-' || upper(hex(randomblob(2))) || '-' ||
            upper(hex(randomblob(6))),
            Id, SourceId, DateCreated, 0
        FROM Characters;

        ALTER TABLE Sources ADD COLUMN SeriesId TEXT REFERENCES Series(Id);

        CREATE TABLE IF NOT EXISTS Characters_New (
            Id                 TEXT    PRIMARY KEY,
            Name               TEXT    NOT NULL,
            DateCreated        TEXT    NOT NULL,
            DateModified       TEXT,
            DateDeleted        TEXT,
            IsDeleted          INTEGER NOT NULL DEFAULT 0,
            ImportBatchId      TEXT    REFERENCES ImportBatches(Id),
            CompletenessStatus TEXT    NOT NULL DEFAULT 'Incomplete'
                               CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview', 'Complete')),
            NoValueKnown       TEXT    NOT NULL DEFAULT '[]'
        );

        INSERT INTO Characters_New (Id, Name, DateCreated, DateModified, DateDeleted, IsDeleted, ImportBatchId, CompletenessStatus, NoValueKnown)
        SELECT Id, Name, DateCreated, DateModified, DateDeleted, IsDeleted, ImportBatchId, CompletenessStatus, NoValueKnown
        FROM Characters;

        DROP TABLE Characters;

        ALTER TABLE Characters_New RENAME TO Characters;

        CREATE INDEX IF NOT EXISTS IX_CharacterSources_CharacterId ON CharacterSources(CharacterId);
        CREATE INDEX IF NOT EXISTS IX_CharacterSources_SourceId    ON CharacterSources(SourceId);
        CREATE INDEX IF NOT EXISTS IX_Series_UniverseId            ON Series(UniverseId);
        CREATE INDEX IF NOT EXISTS IX_Sources_SeriesId              ON Sources(SeriesId);

        ALTER TABLE ImportBatches RENAME COLUMN ImportedBy TO ImportedById;
        """;

    private const string Migration004_ConsolidatedSinceV172 =
        Migration004_ConsolidatedSinceV172Core + CharacterGlobalIdentityMerge;

    // #174, ADR 013 — extracted as its own constant, appended to Migration004_ConsolidatedSinceV172
    // above via string concatenation rather than left fully inline, so a unit test can execute this
    // exact merge logic in isolation against a hand-built precondition state (Characters/
    // CharacterSources/Sources.SeriesId already populated as if migration 4's own schema-creation
    // portion had already run), without needing the full, non-reentrant consolidated migration to
    // execute end to end — see DatabaseInitializerTests' Migration_CharacterGlobalIdentity_* tests.
    // internal (not private): those tests execute this SQL directly against a hand-built connection.
    //
    // Consolidates pre-existing per-Source Character rows into fewer global rows, using the
    // merge-candidate test ADR 013 Decision 1 defines: same Name (case-insensitive — Decision 1(a)),
    // same denormalized SourceType (the Source.Type anchor, ADR 011), and a directly shared,
    // non-null Sources.SeriesId between the two Characters' linked Sources. At this exact point in
    // migration history every pre-existing Character still has exactly one CharacterSources link
    // (the SeriesUniverseSchema portion above performed zero merging), so "the Source this Character
    // is linked to" is unambiguous for every row processed here.
    //
    // Backfills the new SourceType column first (from each row's single linked Source), then computes
    // one canonical survivor per merge group directly via a correlated subquery — no window function
    // needed, since the merge-candidate test is a direct equality test (LOWER(Name), SourceType,
    // SeriesId), not a transitive graph walk. A Character with no Series-known linked Source (NULL
    // SeriesId) is excluded from the merge-groups table entirely, which is what implements
    // Decision 1(c)'s conservative-by-default fallback with no extra special-casing: it simply never
    // becomes a merge candidate for anyone, keeping its own id unchanged.
    //
    // CharacterSources/Quotes.CharacterId are re-pointed to the survivor, then CompletenessStatus
    // resolves to the most-reviewed value across the group (Decision 4); NoValueKnown is set to '[]'
    // directly rather than computed via a JSON-array union (every pre-existing Character row already
    // has NoValueKnown = '[]', since Character's only field is Name, so there is nothing to union
    // yet). Merged-away rows are soft-deleted, never hard-deleted (Decision 3) — CharacterSources/
    // Quotes rows have already been re-pointed away from them by this point, so no FK is left
    // dangling.
    internal const string CharacterGlobalIdentityMerge = """
        ALTER TABLE Characters ADD COLUMN SourceType TEXT NOT NULL DEFAULT 'Unknown'
            CHECK (SourceType IN ('Unknown','Movie','Tv','Anime','Book','Person'));

        UPDATE Characters
        SET SourceType = (
            SELECT s.Type FROM CharacterSources cs JOIN Sources s ON s.Id = cs.SourceId
            WHERE cs.CharacterId = Characters.Id AND cs.IsDeleted = 0 LIMIT 1
        )
        WHERE EXISTS (
            SELECT 1 FROM CharacterSources cs WHERE cs.CharacterId = Characters.Id AND cs.IsDeleted = 0
        );

        CREATE TEMP TABLE Character_MergeGroups AS
        SELECT
            c.Id AS CharacterId,
            (
                SELECT c2.Id
                FROM Characters c2
                JOIN CharacterSources cs2 ON cs2.CharacterId = c2.Id AND cs2.IsDeleted = 0
                JOIN Sources s2          ON s2.Id = cs2.SourceId
                WHERE c2.IsDeleted = 0
                  AND LOWER(c2.Name) = LOWER(c.Name)
                  AND c2.SourceType = c.SourceType
                  AND s2.SeriesId IS NOT NULL
                  AND s2.SeriesId = s.SeriesId
                ORDER BY c2.DateCreated ASC, c2.Id ASC
                LIMIT 1
            ) AS SurvivorId
        FROM Characters c
        JOIN CharacterSources cs ON cs.CharacterId = c.Id AND cs.IsDeleted = 0
        JOIN Sources s           ON s.Id = cs.SourceId
        WHERE c.IsDeleted = 0 AND s.SeriesId IS NOT NULL;

        UPDATE CharacterSources
        SET CharacterId  = (SELECT SurvivorId FROM Character_MergeGroups WHERE CharacterId = CharacterSources.CharacterId),
            DateModified = strftime('%Y-%m-%d %H:%M:%S', 'now')
        WHERE CharacterId IN (SELECT CharacterId FROM Character_MergeGroups WHERE CharacterId <> SurvivorId);

        UPDATE Quotes
        SET CharacterId = (SELECT SurvivorId FROM Character_MergeGroups WHERE CharacterId = Quotes.CharacterId)
        WHERE CharacterId IN (SELECT CharacterId FROM Character_MergeGroups WHERE CharacterId <> SurvivorId);

        UPDATE Characters
        SET CompletenessStatus = (
                SELECT CASE MAX(CASE c2.CompletenessStatus WHEN 'Complete' THEN 3 WHEN 'NeedsReview' THEN 2 ELSE 1 END)
                    WHEN 3 THEN 'Complete'
                    WHEN 2 THEN 'NeedsReview'
                    ELSE 'Incomplete'
                END
                FROM Character_MergeGroups g
                JOIN Characters c2 ON c2.Id = g.CharacterId
                WHERE g.SurvivorId = Characters.Id
            ),
            NoValueKnown = '[]',
            DateModified = strftime('%Y-%m-%d %H:%M:%S', 'now')
        WHERE Id IN (SELECT DISTINCT SurvivorId FROM Character_MergeGroups);

        UPDATE Characters
        SET IsDeleted   = 1,
            DateDeleted  = strftime('%Y-%m-%d %H:%M:%S', 'now'),
            DateModified = strftime('%Y-%m-%d %H:%M:%S', 'now')
        WHERE Id IN (SELECT CharacterId FROM Character_MergeGroups WHERE CharacterId <> SurvivorId);

        DROP TABLE Character_MergeGroups;
        """;

    /// <summary>
    /// #150, ADR 008 — adds a <c>CHECK</c> constraint to <c>ImportBatches.ConflictPolicy</c>
    /// (backed by <see cref="Quotinator.Data.Import.DuplicateResolutionPolicy"/>, a real closed C# enum), closing a gap ADR
    /// 008 itself documented as a known, tracked exception rather than fixing at the time. Migration
    /// 4 already applied to real databases with this column left unconstrained (added there via a
    /// plain <c>ALTER TABLE ... ADD COLUMN</c>, which cannot carry a <c>CHECK</c> retroactively), so
    /// per this project's append-only migration policy the fix is a new migration that rebuilds the
    /// table under a temporary name — the same technique migration 4's own
    /// <c>ImportBatchTypeUserSeed</c> section already uses to widen <c>Type</c>'s <c>CHECK</c>.
    /// <para>
    /// Every code path that constructs an <see cref="Quotinator.Data.Entities.ImportBatchEntity"/> stamps
    /// <c>ConflictPolicy</c> from <c>DuplicateResolutionPolicy.ToString()</c> (PascalCase, e.g.
    /// <c>"NewestWins"</c>) — the same raw-enum-name convention every other CHECK'd enum column in
    /// this project's own tables already uses (<c>Type</c>, <c>Status</c> on this very table). The
    /// column's original <c>ALTER TABLE ... ADD COLUMN ... DEFAULT 'skip'</c> backfill, however, wrote
    /// the literal lowercase default string directly into every pre-existing row at that migration's
    /// own execution time — never through application code, so never PascalCase. The copy step below
    /// normalises that one known legacy value (plus every other enum member, defensively, in case any
    /// row was ever written some other way) to the PascalCase form the new <c>CHECK</c> requires;
    /// anything already PascalCase — or any genuinely unexpected value — passes through the
    /// <c>ELSE</c> branch unchanged, so a truly corrupt value correctly fails the <c>CHECK</c> instead
    /// of being silently miscategorised (same safety property <see cref="ImportConflictMigrations.AddStatusCheckConstraint"/>
    /// already established for this exact class of fix).
    /// </para>
    /// </summary>
    internal const string Migration005_ImportBatchConflictPolicyCheckConstraint = """
        CREATE TABLE ImportBatches_New (
            Id             TEXT    PRIMARY KEY,
            Name           TEXT    NOT NULL,
            Type           TEXT    NOT NULL CHECK (Type IN ('Seed', 'Import', 'System', 'UserSeed')),
            Url            TEXT,
            ImportedAt     TEXT    NOT NULL,
            ImportedById   TEXT,
            RecordCount    INTEGER NOT NULL DEFAULT 0,
            DateCreated    TEXT    NOT NULL,
            DateModified   TEXT,
            DateDeleted    TEXT,
            IsDeleted      INTEGER NOT NULL DEFAULT 0,
            ConflictPolicy TEXT    NOT NULL DEFAULT 'Skip'
                           CHECK (ConflictPolicy IN ('Skip', 'NewestWins', 'MergeOurs', 'MergeTheirs', 'Review')),
            Status         TEXT    NOT NULL DEFAULT 'Applied'
                           CHECK (Status IN ('Staged', 'Applied', 'Discarded')),
            AppliedAt      TEXT
        );

        INSERT INTO ImportBatches_New (Id, Name, Type, Url, ImportedAt, ImportedById, RecordCount, DateCreated, DateModified, DateDeleted, IsDeleted, ConflictPolicy, Status, AppliedAt)
        SELECT
            Id, Name, Type, Url, ImportedAt, ImportedById, RecordCount, DateCreated, DateModified, DateDeleted, IsDeleted,
            CASE ConflictPolicy
                WHEN 'skip'         THEN 'Skip'
                WHEN 'newest-wins'  THEN 'NewestWins'
                WHEN 'merge-ours'   THEN 'MergeOurs'
                WHEN 'merge-theirs' THEN 'MergeTheirs'
                WHEN 'review'       THEN 'Review'
                ELSE ConflictPolicy
            END,
            Status, AppliedAt
        FROM ImportBatches;

        DROP TABLE ImportBatches;

        ALTER TABLE ImportBatches_New RENAME TO ImportBatches;
        """;

    /// <summary>
    /// #253/#254/ADR 015 — migrates <c>ImportBatches</c>' data into <c>Import_Batch</c> (created empty
    /// by <see cref="Quotinator.Data.Database.DomainPrefixRenameMigrations.RenameDataOwnedTables"/>,
    /// which always runs first), drops <c>ImportBatches</c>, and rebuilds every one of
    /// Quotinator.Core's own 17 domain tables under its final <c>Quotinator_</c>-prefixed name (#254),
    /// with every <c>REFERENCES</c> clause — to <c>Import_Batch</c> and to each other — pointing at the
    /// final name. A new migration, not a rewrite of migration 5 — found live during #254's own T1
    /// pass (2026-08-02): migration 5 (#150's CHECK constraint) had already run against a real
    /// database (this project's own local dev database, not necessarily a public release — this
    /// project's "never edit an existing migration" policy applies to any database that may have
    /// already run it, not only ones covered by a git tag) before this rename work was designed, so
    /// rewriting its content in place — even though it had never shipped in a tagged release — left
    /// that database's already-recorded version 5 silently out of sync with the new content. The Data
    /// copy step below does not need migration 5's own <c>ConflictPolicy</c> casing normalisation
    /// (<c>'skip'</c> → <c>'Skip'</c> etc.) repeated — migration 5 always runs before this one for
    /// every database, whether freshly (upgrading straight from a release that never had migration 5
    /// at all) or previously (a database that already ran it), so <c>ImportBatches.ConflictPolicy</c>
    /// is guaranteed already PascalCase by the time this migration's own <c>SELECT</c> runs.
    /// </summary>
    /// <remarks>
    /// <c>ImportBatches</c> → <c>Import_Batch</c> can't be a same-table rename here (the way every
    /// other #253/#254 table rename is one) without breaking the ordering constraint documented on
    /// <c>RenameDataOwnedTables</c> — so this migration instead copies the data across after Data's
    /// migration has already created the empty destination table. That copy-then-drop means
    /// <c>ImportBatches</c> is a genuinely different physical table from <c>Import_Batch</c>, not a
    /// renamed one — and SQLite only auto-converts a <c>FOREIGN KEY</c> declaration in another table
    /// when the table it references is renamed via <c>ALTER TABLE ... RENAME TO</c>, never when it's
    /// dropped and a differently-named replacement created (confirmed against sqlite.org, 2026-08-01).
    /// Left unaddressed, every one of Quotinator.Core's domain tables with a non-null
    /// <c>ImportBatchId</c> column would keep its existing <c>REFERENCES ImportBatches(Id)</c>
    /// declaration, and every future <c>INSERT</c>/<c>UPDATE</c> on it would throw
    /// <c>no such table: ImportBatches</c> once migrations finish and <c>PRAGMA foreign_keys</c> is
    /// back on — confirmed with a direct, throwaway repro against a real SQLite connection before
    /// writing this fix. #254's own 17-table domain-prefix rename needs the identical
    /// create-new/copy/drop/rename technique regardless (a plain <c>ALTER TABLE ... RENAME TO</c> only
    /// auto-updates *inbound* FK references from other tables, never a renamed table's own *outbound*
    /// ones — so e.g. <c>ConversationLines</c>' own <c>QuoteId REFERENCES Quotes(Id)</c> clause needs
    /// rebuilding to say <c>Quotinator_Quote</c> even though nothing about <c>ConversationLines</c>
    /// itself changed shape), so both fixes are folded into one rebuild pass per table rather than
    /// rebuilding each table twice. Every table is rebuilt in FK dependency order — a table's own
    /// <c>CREATE ..._New</c> can only reference another table's *final* name if that other table has
    /// already been rebuilt earlier in this same script: <c>Universe</c> → <c>Series</c> →
    /// <c>Sources</c> → <c>Characters</c>/<c>People</c> → <c>Quotes</c> → <c>Conversations</c>/
    /// <c>StageDirections</c>/<c>SoundCues</c> → <c>SourceTranslations</c>/
    /// <c>CharacterTranslations</c>/<c>CharacterSources</c> → <c>QuoteTranslations</c>/
    /// <c>QuoteGenres</c> → <c>StageDirectionTranslations</c>/<c>SoundCueTranslations</c> →
    /// <c>ConversationLines</c> (the last, since it FK-references four other renamed tables at once) —
    /// even though <c>PRAGMA foreign_keys</c> is off for the duration of every migration, purely so
    /// this text reads the same way the schema itself is structured.
    /// </remarks>
    internal const string Migration006_DomainPrefixRename = """
        INSERT INTO Import_Batch (Id, Name, Type, Url, ImportedAt, ImportedById, RecordCount, DateCreated, DateModified, DateDeleted, IsDeleted, ConflictPolicy, Status, AppliedAt)
        SELECT Id, Name, Type, Url, ImportedAt, ImportedById, RecordCount, DateCreated, DateModified, DateDeleted, IsDeleted, ConflictPolicy, Status, AppliedAt
        FROM ImportBatches;

        DROP TABLE ImportBatches;

        CREATE TABLE IF NOT EXISTS Universe_New (
            Id                 TEXT    PRIMARY KEY,
            Name               TEXT    NOT NULL UNIQUE,
            ImportBatchId      TEXT    REFERENCES Import_Batch(Id),
            CompletenessStatus TEXT    NOT NULL DEFAULT 'Incomplete'
                               CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview', 'Complete')),
            NoValueKnown       TEXT    NOT NULL DEFAULT '[]',
            DateCreated        TEXT    NOT NULL,
            DateModified       TEXT,
            DateDeleted        TEXT,
            IsDeleted          INTEGER NOT NULL DEFAULT 0
        );
        INSERT INTO Universe_New (Id, Name, ImportBatchId, CompletenessStatus, NoValueKnown, DateCreated, DateModified, DateDeleted, IsDeleted)
        SELECT Id, Name, ImportBatchId, CompletenessStatus, NoValueKnown, DateCreated, DateModified, DateDeleted, IsDeleted FROM Universe;
        DROP TABLE Universe;
        ALTER TABLE Universe_New RENAME TO Quotinator_Universe;

        CREATE TABLE IF NOT EXISTS Series_New (
            Id                 TEXT    PRIMARY KEY,
            Name               TEXT    NOT NULL UNIQUE,
            UniverseId         TEXT    REFERENCES Quotinator_Universe(Id),
            ImportBatchId      TEXT    REFERENCES Import_Batch(Id),
            CompletenessStatus TEXT    NOT NULL DEFAULT 'Incomplete'
                               CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview', 'Complete')),
            NoValueKnown       TEXT    NOT NULL DEFAULT '[]',
            DateCreated        TEXT    NOT NULL,
            DateModified       TEXT,
            DateDeleted        TEXT,
            IsDeleted          INTEGER NOT NULL DEFAULT 0
        );
        INSERT INTO Series_New (Id, Name, UniverseId, ImportBatchId, CompletenessStatus, NoValueKnown, DateCreated, DateModified, DateDeleted, IsDeleted)
        SELECT Id, Name, UniverseId, ImportBatchId, CompletenessStatus, NoValueKnown, DateCreated, DateModified, DateDeleted, IsDeleted FROM Series;
        DROP TABLE Series;
        ALTER TABLE Series_New RENAME TO Quotinator_Series;
        CREATE INDEX IF NOT EXISTS IX_Quotinator_Series_UniverseId ON Quotinator_Series(UniverseId);

        CREATE TABLE IF NOT EXISTS Sources_New (
            Id           TEXT    PRIMARY KEY,
            Title        TEXT    NOT NULL,
            Type         TEXT    NOT NULL DEFAULT 'Movie'
                         CHECK (Type IN ('Unknown','Movie','Tv','Anime','Book','Person')),
            Date         TEXT,
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0,
            ImportBatchId TEXT   REFERENCES Import_Batch(Id),
            CompletenessStatus TEXT NOT NULL DEFAULT 'Incomplete'
                         CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview', 'Complete')),
            NoValueKnown TEXT    NOT NULL DEFAULT '[]',
            SeriesId     TEXT    REFERENCES Quotinator_Series(Id),
            UNIQUE (Title, Type)
        );
        INSERT INTO Sources_New (Id, Title, Type, Date, DateCreated, DateModified, DateDeleted, IsDeleted, ImportBatchId, CompletenessStatus, NoValueKnown, SeriesId)
        SELECT Id, Title, Type, Date, DateCreated, DateModified, DateDeleted, IsDeleted, ImportBatchId, CompletenessStatus, NoValueKnown, SeriesId FROM Sources;
        DROP TABLE Sources;
        ALTER TABLE Sources_New RENAME TO Quotinator_Source;
        CREATE INDEX IF NOT EXISTS IX_Quotinator_Source_SeriesId ON Quotinator_Source(SeriesId);

        CREATE TABLE IF NOT EXISTS Characters_New (
            Id           TEXT    PRIMARY KEY,
            Name         TEXT    NOT NULL,
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0,
            ImportBatchId TEXT   REFERENCES Import_Batch(Id),
            CompletenessStatus TEXT NOT NULL DEFAULT 'Incomplete'
                         CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview', 'Complete')),
            NoValueKnown TEXT    NOT NULL DEFAULT '[]',
            SourceType   TEXT    NOT NULL DEFAULT 'Unknown'
                         CHECK (SourceType IN ('Unknown','Movie','Tv','Anime','Book','Person'))
        );
        INSERT INTO Characters_New (Id, Name, DateCreated, DateModified, DateDeleted, IsDeleted, ImportBatchId, CompletenessStatus, NoValueKnown, SourceType)
        SELECT Id, Name, DateCreated, DateModified, DateDeleted, IsDeleted, ImportBatchId, CompletenessStatus, NoValueKnown, SourceType FROM Characters;
        DROP TABLE Characters;
        ALTER TABLE Characters_New RENAME TO Quotinator_Character;

        CREATE TABLE IF NOT EXISTS People_New (
            Id           TEXT    PRIMARY KEY,
            Name         TEXT    NOT NULL UNIQUE,
            DateOfBirth  TEXT,
            DateOfDeath  TEXT,
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0,
            ImportBatchId TEXT   REFERENCES Import_Batch(Id),
            CompletenessStatus TEXT NOT NULL DEFAULT 'Incomplete'
                         CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview', 'Complete')),
            NoValueKnown TEXT    NOT NULL DEFAULT '[]'
        );
        INSERT INTO People_New (Id, Name, DateOfBirth, DateOfDeath, DateCreated, DateModified, DateDeleted, IsDeleted, ImportBatchId, CompletenessStatus, NoValueKnown)
        SELECT Id, Name, DateOfBirth, DateOfDeath, DateCreated, DateModified, DateDeleted, IsDeleted, ImportBatchId, CompletenessStatus, NoValueKnown FROM People;
        DROP TABLE People;
        ALTER TABLE People_New RENAME TO Quotinator_Person;

        CREATE TABLE IF NOT EXISTS Quotes_New (
            Id               TEXT    PRIMARY KEY,
            QuoteText        TEXT    NOT NULL,
            OriginalLanguage TEXT    NOT NULL DEFAULT 'en',
            SourceId         TEXT    NOT NULL REFERENCES Quotinator_Source(Id),
            CharacterId      TEXT    REFERENCES Quotinator_Character(Id),
            PersonId         TEXT    REFERENCES Quotinator_Person(Id),
            DateCreated      TEXT    NOT NULL,
            DateModified     TEXT,
            DateDeleted      TEXT,
            IsDeleted        INTEGER NOT NULL DEFAULT 0,
            ImportBatchId    TEXT    REFERENCES Import_Batch(Id),
            CompletenessStatus TEXT  NOT NULL DEFAULT 'Incomplete'
                             CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview', 'Complete')),
            NoValueKnown     TEXT    NOT NULL DEFAULT '[]'
        );
        INSERT INTO Quotes_New (Id, QuoteText, OriginalLanguage, SourceId, CharacterId, PersonId, DateCreated, DateModified, DateDeleted, IsDeleted, ImportBatchId, CompletenessStatus, NoValueKnown)
        SELECT Id, QuoteText, OriginalLanguage, SourceId, CharacterId, PersonId, DateCreated, DateModified, DateDeleted, IsDeleted, ImportBatchId, CompletenessStatus, NoValueKnown FROM Quotes;
        DROP TABLE Quotes;
        ALTER TABLE Quotes_New RENAME TO Quotinator_Quote;

        CREATE TABLE IF NOT EXISTS Conversations_New (
            Id                 TEXT    PRIMARY KEY,
            Description        TEXT,
            ImportBatchId      TEXT    REFERENCES Import_Batch(Id),
            CompletenessStatus TEXT    NOT NULL DEFAULT 'Incomplete'
                               CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview', 'Complete')),
            NoValueKnown       TEXT    NOT NULL DEFAULT '[]',
            DateCreated        TEXT    NOT NULL,
            DateModified       TEXT,
            DateDeleted        TEXT,
            IsDeleted          INTEGER NOT NULL DEFAULT 0
        );
        INSERT INTO Conversations_New (Id, Description, ImportBatchId, CompletenessStatus, NoValueKnown, DateCreated, DateModified, DateDeleted, IsDeleted)
        SELECT Id, Description, ImportBatchId, CompletenessStatus, NoValueKnown, DateCreated, DateModified, DateDeleted, IsDeleted FROM Conversations;
        DROP TABLE Conversations;
        ALTER TABLE Conversations_New RENAME TO Quotinator_Conversation;

        CREATE TABLE IF NOT EXISTS StageDirections_New (
            Id                 TEXT    PRIMARY KEY,
            Text               TEXT    NOT NULL,
            ImageUrl           TEXT,
            ImportBatchId      TEXT    REFERENCES Import_Batch(Id),
            CompletenessStatus TEXT    NOT NULL DEFAULT 'Incomplete'
                               CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview', 'Complete')),
            NoValueKnown       TEXT    NOT NULL DEFAULT '[]',
            DateCreated        TEXT    NOT NULL,
            DateModified       TEXT,
            DateDeleted        TEXT,
            IsDeleted          INTEGER NOT NULL DEFAULT 0
        );
        INSERT INTO StageDirections_New (Id, Text, ImageUrl, ImportBatchId, CompletenessStatus, NoValueKnown, DateCreated, DateModified, DateDeleted, IsDeleted)
        SELECT Id, Text, ImageUrl, ImportBatchId, CompletenessStatus, NoValueKnown, DateCreated, DateModified, DateDeleted, IsDeleted FROM StageDirections;
        DROP TABLE StageDirections;
        ALTER TABLE StageDirections_New RENAME TO Quotinator_StageDirection;

        CREATE TABLE IF NOT EXISTS SoundCues_New (
            Id                 TEXT    PRIMARY KEY,
            Text               TEXT    NOT NULL,
            SoundFileUrl       TEXT,
            ImageUrl           TEXT,
            ImportBatchId      TEXT    REFERENCES Import_Batch(Id),
            CompletenessStatus TEXT    NOT NULL DEFAULT 'Incomplete'
                               CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview', 'Complete')),
            NoValueKnown       TEXT    NOT NULL DEFAULT '[]',
            DateCreated        TEXT    NOT NULL,
            DateModified       TEXT,
            DateDeleted        TEXT,
            IsDeleted          INTEGER NOT NULL DEFAULT 0
        );
        INSERT INTO SoundCues_New (Id, Text, SoundFileUrl, ImageUrl, ImportBatchId, CompletenessStatus, NoValueKnown, DateCreated, DateModified, DateDeleted, IsDeleted)
        SELECT Id, Text, SoundFileUrl, ImageUrl, ImportBatchId, CompletenessStatus, NoValueKnown, DateCreated, DateModified, DateDeleted, IsDeleted FROM SoundCues;
        DROP TABLE SoundCues;
        ALTER TABLE SoundCues_New RENAME TO Quotinator_SoundCue;

        CREATE TABLE IF NOT EXISTS SourceTranslations_New (
            Id           TEXT    PRIMARY KEY,
            SourceId     TEXT    NOT NULL REFERENCES Quotinator_Source(Id),
            Language     TEXT    NOT NULL,
            Title        TEXT    NOT NULL,
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0,
            UNIQUE (SourceId, Language)
        );
        INSERT INTO SourceTranslations_New (Id, SourceId, Language, Title, DateCreated, DateModified, DateDeleted, IsDeleted)
        SELECT Id, SourceId, Language, Title, DateCreated, DateModified, DateDeleted, IsDeleted FROM SourceTranslations;
        DROP TABLE SourceTranslations;
        ALTER TABLE SourceTranslations_New RENAME TO Quotinator_SourceTranslation;

        CREATE TABLE IF NOT EXISTS CharacterTranslations_New (
            Id           TEXT    PRIMARY KEY,
            CharacterId  TEXT    NOT NULL REFERENCES Quotinator_Character(Id),
            Language     TEXT    NOT NULL,
            Name         TEXT    NOT NULL,
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0,
            UNIQUE (CharacterId, Language)
        );
        INSERT INTO CharacterTranslations_New (Id, CharacterId, Language, Name, DateCreated, DateModified, DateDeleted, IsDeleted)
        SELECT Id, CharacterId, Language, Name, DateCreated, DateModified, DateDeleted, IsDeleted FROM CharacterTranslations;
        DROP TABLE CharacterTranslations;
        ALTER TABLE CharacterTranslations_New RENAME TO Quotinator_CharacterTranslation;

        CREATE TABLE IF NOT EXISTS CharacterSources_New (
            Id           TEXT    PRIMARY KEY,
            CharacterId  TEXT    NOT NULL REFERENCES Quotinator_Character(Id),
            SourceId     TEXT    NOT NULL REFERENCES Quotinator_Source(Id),
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0,
            UNIQUE (CharacterId, SourceId)
        );
        INSERT INTO CharacterSources_New (Id, CharacterId, SourceId, DateCreated, DateModified, DateDeleted, IsDeleted)
        SELECT Id, CharacterId, SourceId, DateCreated, DateModified, DateDeleted, IsDeleted FROM CharacterSources;
        DROP TABLE CharacterSources;
        ALTER TABLE CharacterSources_New RENAME TO Quotinator_CharacterSource;
        CREATE INDEX IF NOT EXISTS IX_Quotinator_CharacterSource_CharacterId ON Quotinator_CharacterSource(CharacterId);
        CREATE INDEX IF NOT EXISTS IX_Quotinator_CharacterSource_SourceId    ON Quotinator_CharacterSource(SourceId);

        CREATE TABLE IF NOT EXISTS QuoteTranslations_New (
            Id           TEXT    PRIMARY KEY,
            QuoteId      TEXT    NOT NULL REFERENCES Quotinator_Quote(Id),
            Language     TEXT    NOT NULL,
            QuoteText    TEXT    NOT NULL,
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0,
            UNIQUE (QuoteId, Language)
        );
        INSERT INTO QuoteTranslations_New (Id, QuoteId, Language, QuoteText, DateCreated, DateModified, DateDeleted, IsDeleted)
        SELECT Id, QuoteId, Language, QuoteText, DateCreated, DateModified, DateDeleted, IsDeleted FROM QuoteTranslations;
        DROP TABLE QuoteTranslations;
        ALTER TABLE QuoteTranslations_New RENAME TO Quotinator_QuoteTranslation;

        CREATE TABLE IF NOT EXISTS QuoteGenres_New (
            Id           TEXT    PRIMARY KEY,
            QuoteId      TEXT    NOT NULL REFERENCES Quotinator_Quote(Id),
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
        INSERT INTO QuoteGenres_New (Id, QuoteId, Genre, DateCreated, DateModified, DateDeleted, IsDeleted)
        SELECT Id, QuoteId, Genre, DateCreated, DateModified, DateDeleted, IsDeleted FROM QuoteGenres;
        DROP TABLE QuoteGenres;
        ALTER TABLE QuoteGenres_New RENAME TO Quotinator_QuoteGenre;

        CREATE TABLE IF NOT EXISTS StageDirectionTranslations_New (
            Id               TEXT    PRIMARY KEY,
            StageDirectionId TEXT    NOT NULL REFERENCES Quotinator_StageDirection(Id),
            Language         TEXT    NOT NULL,
            Text             TEXT    NOT NULL,
            DateCreated      TEXT    NOT NULL,
            DateModified     TEXT,
            DateDeleted      TEXT,
            IsDeleted        INTEGER NOT NULL DEFAULT 0,
            UNIQUE (StageDirectionId, Language)
        );
        INSERT INTO StageDirectionTranslations_New (Id, StageDirectionId, Language, Text, DateCreated, DateModified, DateDeleted, IsDeleted)
        SELECT Id, StageDirectionId, Language, Text, DateCreated, DateModified, DateDeleted, IsDeleted FROM StageDirectionTranslations;
        DROP TABLE StageDirectionTranslations;
        ALTER TABLE StageDirectionTranslations_New RENAME TO Quotinator_StageDirectionTranslation;
        CREATE INDEX IF NOT EXISTS IX_Quotinator_StageDirectionTranslation_StageDirectionId ON Quotinator_StageDirectionTranslation(StageDirectionId);

        CREATE TABLE IF NOT EXISTS SoundCueTranslations_New (
            Id           TEXT    PRIMARY KEY,
            SoundCueId   TEXT    NOT NULL REFERENCES Quotinator_SoundCue(Id),
            Language     TEXT    NOT NULL,
            Text         TEXT    NOT NULL,
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0,
            UNIQUE (SoundCueId, Language)
        );
        INSERT INTO SoundCueTranslations_New (Id, SoundCueId, Language, Text, DateCreated, DateModified, DateDeleted, IsDeleted)
        SELECT Id, SoundCueId, Language, Text, DateCreated, DateModified, DateDeleted, IsDeleted FROM SoundCueTranslations;
        DROP TABLE SoundCueTranslations;
        ALTER TABLE SoundCueTranslations_New RENAME TO Quotinator_SoundCueTranslation;
        CREATE INDEX IF NOT EXISTS IX_Quotinator_SoundCueTranslation_SoundCueId ON Quotinator_SoundCueTranslation(SoundCueId);

        CREATE TABLE IF NOT EXISTS ConversationLines_New (
            Id                TEXT    PRIMARY KEY,
            ConversationId    TEXT    NOT NULL REFERENCES Quotinator_Conversation(Id),
            [Order]           INTEGER NOT NULL,
            LineType          TEXT    NOT NULL
                              CHECK (LineType IN ('Quote','StageDirection','SoundCue')),
            QuoteId           TEXT    REFERENCES Quotinator_Quote(Id),
            StageDirectionId  TEXT    REFERENCES Quotinator_StageDirection(Id),
            SoundCueId        TEXT    REFERENCES Quotinator_SoundCue(Id),
            DateCreated       TEXT    NOT NULL,
            DateModified      TEXT,
            DateDeleted       TEXT,
            IsDeleted         INTEGER NOT NULL DEFAULT 0,
            CHECK (
                (LineType = 'Quote'          AND QuoteId          IS NOT NULL AND StageDirectionId IS NULL AND SoundCueId IS NULL) OR
                (LineType = 'StageDirection' AND StageDirectionId IS NOT NULL AND QuoteId          IS NULL AND SoundCueId IS NULL) OR
                (LineType = 'SoundCue'       AND SoundCueId       IS NOT NULL AND QuoteId          IS NULL AND StageDirectionId IS NULL)
            ),
            UNIQUE (ConversationId, [Order])
        );
        INSERT INTO ConversationLines_New (Id, ConversationId, [Order], LineType, QuoteId, StageDirectionId, SoundCueId, DateCreated, DateModified, DateDeleted, IsDeleted)
        SELECT Id, ConversationId, [Order], LineType, QuoteId, StageDirectionId, SoundCueId, DateCreated, DateModified, DateDeleted, IsDeleted FROM ConversationLines;
        DROP TABLE ConversationLines;
        ALTER TABLE ConversationLines_New RENAME TO Quotinator_ConversationLine;
        CREATE INDEX IF NOT EXISTS IX_Quotinator_ConversationLine_ConversationId   ON Quotinator_ConversationLine(ConversationId);
        CREATE INDEX IF NOT EXISTS IX_Quotinator_ConversationLine_QuoteId          ON Quotinator_ConversationLine(QuoteId);
        CREATE INDEX IF NOT EXISTS IX_Quotinator_ConversationLine_StageDirectionId ON Quotinator_ConversationLine(StageDirectionId);
        CREATE INDEX IF NOT EXISTS IX_Quotinator_ConversationLine_SoundCueId       ON Quotinator_ConversationLine(SoundCueId);
        """;

    // Consolidated schema for a genuinely fresh database — the union of migrations 1-8's final
    // result, with ImportBatchId baked directly into the four entity tables (migration003's
    // ALTER TABLE ADD COLUMN always appends, so it's listed last here to match column order),
    // ImportBatches using the final widened CHECK constraint (migration004), ImportBatches.
    // ConflictPolicy (originally migration005's ALTER TABLE ADD COLUMN, also always appends, so it
    // was listed last too) present with the CHECK constraint the real, later
    // Migration005_ImportBatchConflictPolicyCheckConstraint (#150, ADR 008) adds — this baseline
    // always reflects the column's final, current-day shape, not its shape as of the historical
    // migration numbered 5 in this comment's own narrative (that number predates #155's consolidation
    // and refers to a different, squashed-away migration; the real, currently-applied Migration005 is
    // the CHECK-constraint one), CompletenessStatus/NoValueKnown
    // (migration006's ALTER TABLE ADD COLUMN, appended last again, revised from a plain IsComplete
    // BIT to the 3-state enum by #165 before ever shipping) on the four entity tables, and
    // ImportBatches.Status/AppliedAt (migration007's ALTER TABLE ADD COLUMN, appended last) with the
    // same 'Applied' default backfill value, and migration008's Conversations/ConversationLines/
    // StageDirections/StageDirectionTranslations/SoundCues/SoundCueTranslations tables verbatim
    // (all created via CREATE TABLE, so no column-ordering caveat applies to them) — Conversations/
    // StageDirections/SoundCues also carry CompletenessStatus/NoValueKnown inline (#165), added
    // directly to migration008 rather than via a later ALTER since these three tables didn't exist
    // before it. Migration009's Universe/Series/CharacterSources tables and Sources.SeriesId (#179,
    // ADR 011) are also included verbatim — SeriesId is an ALTER TABLE ADD COLUMN on Sources, so it
    // is listed last on that table to match column order, same as every other ALTER-appended column
    // above. Characters no longer carries SourceId or UNIQUE(SourceId, Name) — both dropped by
    // migration009's rebuild. Migration010's ImportedBy -> ImportedById rename (#213) is folded in
    // directly — ImportBatches.ImportedById is created under its final name, since a RENAME COLUMN
    // has nothing left to rename against on a table that never had the old name. Migration011's
    // Characters.SourceType (#174, ADR 013) is also included directly — again an ALTER TABLE ADD
    // COLUMN, listed last on Characters to match column order.
    // Deliberately omits migration002's DELETE FROM QuoteGenres (data-repair for pre-existing bad
    // data — nothing to repair on a fresh database), migration003's pre-seed INSERTs (WHERE
    // EXISTS-guarded, always a no-op before any quote has been seeded), migration009's
    // CharacterSources backfill INSERT (nothing to backfill on a fresh database — Characters is
    // always empty at baseline time), and migration011's own merge-consolidation UPDATEs (nothing to
    // consolidate on a fresh database — same reasoning). Kept in sync with migrations 1-11 (the
    // pre-#155 historical narrative numbering) plus the real, current Migration005 and Migration006
    // by DatabaseInitializerTests' schema-drift comparison.
    // #253/#254/ADR 015 — Import_Batch (formerly ImportBatches) is created by Quotinator.Data's own
    // DataBaselineSql, not here — Data's baseline always runs before this one in the same
    // ApplyBaselineAsync call, so it already exists by the time any REFERENCES Import_Batch(Id) clause
    // below is evaluated. Only the C# entity/repository types and the table's creation moved to Data
    // (ADR 004/015); every table below still carries its own ImportBatchId FK column here, since these
    // remain Quotinator.Core's own domain tables, now created directly under their final
    // Quotinator_-prefixed name (#254) instead of the historical create-then-rename dance.
    private const string BaselineSchema = """
        CREATE TABLE IF NOT EXISTS Quotinator_Universe (
            Id                 TEXT    PRIMARY KEY,
            Name               TEXT    NOT NULL UNIQUE,
            ImportBatchId      TEXT    REFERENCES Import_Batch(Id),
            CompletenessStatus TEXT    NOT NULL DEFAULT 'Incomplete'
                               CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview', 'Complete')),
            NoValueKnown       TEXT    NOT NULL DEFAULT '[]',
            DateCreated        TEXT    NOT NULL,
            DateModified       TEXT,
            DateDeleted        TEXT,
            IsDeleted          INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS Quotinator_Series (
            Id                 TEXT    PRIMARY KEY,
            Name               TEXT    NOT NULL UNIQUE,
            UniverseId         TEXT    REFERENCES Quotinator_Universe(Id),
            ImportBatchId      TEXT    REFERENCES Import_Batch(Id),
            CompletenessStatus TEXT    NOT NULL DEFAULT 'Incomplete'
                               CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview', 'Complete')),
            NoValueKnown       TEXT    NOT NULL DEFAULT '[]',
            DateCreated        TEXT    NOT NULL,
            DateModified       TEXT,
            DateDeleted        TEXT,
            IsDeleted          INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS Quotinator_Source (
            Id           TEXT    PRIMARY KEY,
            Title        TEXT    NOT NULL,
            Type         TEXT    NOT NULL DEFAULT 'Movie'
                         CHECK (Type IN ('Unknown','Movie','Tv','Anime','Book','Person')),
            Date         TEXT,
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0,
            ImportBatchId TEXT   REFERENCES Import_Batch(Id),
            CompletenessStatus TEXT NOT NULL DEFAULT 'Incomplete'
                         CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview', 'Complete')),
            NoValueKnown TEXT    NOT NULL DEFAULT '[]',
            SeriesId     TEXT    REFERENCES Quotinator_Series(Id),
            UNIQUE (Title, Type)
        );

        CREATE TABLE IF NOT EXISTS Quotinator_SourceTranslation (
            Id           TEXT    PRIMARY KEY,
            SourceId     TEXT    NOT NULL REFERENCES Quotinator_Source(Id),
            Language     TEXT    NOT NULL,
            Title        TEXT    NOT NULL,
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0,
            UNIQUE (SourceId, Language)
        );

        CREATE TABLE IF NOT EXISTS Quotinator_Character (
            Id           TEXT    PRIMARY KEY,
            Name         TEXT    NOT NULL,
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0,
            ImportBatchId TEXT   REFERENCES Import_Batch(Id),
            CompletenessStatus TEXT NOT NULL DEFAULT 'Incomplete'
                         CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview', 'Complete')),
            NoValueKnown TEXT    NOT NULL DEFAULT '[]',
            SourceType   TEXT    NOT NULL DEFAULT 'Unknown'
                         CHECK (SourceType IN ('Unknown','Movie','Tv','Anime','Book','Person'))
        );

        CREATE TABLE IF NOT EXISTS Quotinator_CharacterTranslation (
            Id           TEXT    PRIMARY KEY,
            CharacterId  TEXT    NOT NULL REFERENCES Quotinator_Character(Id),
            Language     TEXT    NOT NULL,
            Name         TEXT    NOT NULL,
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0,
            UNIQUE (CharacterId, Language)
        );

        CREATE TABLE IF NOT EXISTS Quotinator_CharacterSource (
            Id           TEXT    PRIMARY KEY,
            CharacterId  TEXT    NOT NULL REFERENCES Quotinator_Character(Id),
            SourceId     TEXT    NOT NULL REFERENCES Quotinator_Source(Id),
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0,
            UNIQUE (CharacterId, SourceId)
        );

        CREATE TABLE IF NOT EXISTS Quotinator_Person (
            Id           TEXT    PRIMARY KEY,
            Name         TEXT    NOT NULL UNIQUE,
            DateOfBirth  TEXT,
            DateOfDeath  TEXT,
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0,
            ImportBatchId TEXT   REFERENCES Import_Batch(Id),
            CompletenessStatus TEXT NOT NULL DEFAULT 'Incomplete'
                         CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview', 'Complete')),
            NoValueKnown TEXT    NOT NULL DEFAULT '[]'
        );

        CREATE TABLE IF NOT EXISTS Quotinator_Quote (
            Id               TEXT    PRIMARY KEY,
            QuoteText        TEXT    NOT NULL,
            OriginalLanguage TEXT    NOT NULL DEFAULT 'en',
            SourceId         TEXT    NOT NULL REFERENCES Quotinator_Source(Id),
            CharacterId      TEXT    REFERENCES Quotinator_Character(Id),
            PersonId         TEXT    REFERENCES Quotinator_Person(Id),
            DateCreated      TEXT    NOT NULL,
            DateModified     TEXT,
            DateDeleted      TEXT,
            IsDeleted        INTEGER NOT NULL DEFAULT 0,
            ImportBatchId    TEXT    REFERENCES Import_Batch(Id),
            CompletenessStatus TEXT  NOT NULL DEFAULT 'Incomplete'
                             CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview', 'Complete')),
            NoValueKnown     TEXT    NOT NULL DEFAULT '[]'
        );

        CREATE TABLE IF NOT EXISTS Quotinator_QuoteTranslation (
            Id           TEXT    PRIMARY KEY,
            QuoteId      TEXT    NOT NULL REFERENCES Quotinator_Quote(Id),
            Language     TEXT    NOT NULL,
            QuoteText    TEXT    NOT NULL,
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0,
            UNIQUE (QuoteId, Language)
        );

        CREATE TABLE IF NOT EXISTS Quotinator_QuoteGenre (
            Id           TEXT    PRIMARY KEY,
            QuoteId      TEXT    NOT NULL REFERENCES Quotinator_Quote(Id),
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

        CREATE TABLE IF NOT EXISTS Quotinator_Conversation (
            Id                 TEXT    PRIMARY KEY,
            Description        TEXT,
            ImportBatchId      TEXT    REFERENCES Import_Batch(Id),
            CompletenessStatus TEXT    NOT NULL DEFAULT 'Incomplete'
                               CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview', 'Complete')),
            NoValueKnown       TEXT    NOT NULL DEFAULT '[]',
            DateCreated        TEXT    NOT NULL,
            DateModified       TEXT,
            DateDeleted        TEXT,
            IsDeleted          INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS Quotinator_StageDirection (
            Id                 TEXT    PRIMARY KEY,
            Text               TEXT    NOT NULL,
            ImageUrl           TEXT,
            ImportBatchId      TEXT    REFERENCES Import_Batch(Id),
            CompletenessStatus TEXT    NOT NULL DEFAULT 'Incomplete'
                               CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview', 'Complete')),
            NoValueKnown       TEXT    NOT NULL DEFAULT '[]',
            DateCreated        TEXT    NOT NULL,
            DateModified       TEXT,
            DateDeleted        TEXT,
            IsDeleted          INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS Quotinator_StageDirectionTranslation (
            Id               TEXT    PRIMARY KEY,
            StageDirectionId TEXT    NOT NULL REFERENCES Quotinator_StageDirection(Id),
            Language         TEXT    NOT NULL,
            Text             TEXT    NOT NULL,
            DateCreated      TEXT    NOT NULL,
            DateModified     TEXT,
            DateDeleted      TEXT,
            IsDeleted        INTEGER NOT NULL DEFAULT 0,
            UNIQUE (StageDirectionId, Language)
        );

        CREATE TABLE IF NOT EXISTS Quotinator_SoundCue (
            Id                 TEXT    PRIMARY KEY,
            Text               TEXT    NOT NULL,
            SoundFileUrl       TEXT,
            ImageUrl           TEXT,
            ImportBatchId      TEXT    REFERENCES Import_Batch(Id),
            CompletenessStatus TEXT    NOT NULL DEFAULT 'Incomplete'
                               CHECK (CompletenessStatus IN ('Incomplete', 'NeedsReview', 'Complete')),
            NoValueKnown       TEXT    NOT NULL DEFAULT '[]',
            DateCreated        TEXT    NOT NULL,
            DateModified       TEXT,
            DateDeleted        TEXT,
            IsDeleted          INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS Quotinator_SoundCueTranslation (
            Id           TEXT    PRIMARY KEY,
            SoundCueId   TEXT    NOT NULL REFERENCES Quotinator_SoundCue(Id),
            Language     TEXT    NOT NULL,
            Text         TEXT    NOT NULL,
            DateCreated  TEXT    NOT NULL,
            DateModified TEXT,
            DateDeleted  TEXT,
            IsDeleted    INTEGER NOT NULL DEFAULT 0,
            UNIQUE (SoundCueId, Language)
        );

        CREATE TABLE IF NOT EXISTS Quotinator_ConversationLine (
            Id                TEXT    PRIMARY KEY,
            ConversationId    TEXT    NOT NULL REFERENCES Quotinator_Conversation(Id),
            [Order]           INTEGER NOT NULL,
            LineType          TEXT    NOT NULL
                              CHECK (LineType IN ('Quote','StageDirection','SoundCue')),
            QuoteId           TEXT    REFERENCES Quotinator_Quote(Id),
            StageDirectionId  TEXT    REFERENCES Quotinator_StageDirection(Id),
            SoundCueId        TEXT    REFERENCES Quotinator_SoundCue(Id),
            DateCreated       TEXT    NOT NULL,
            DateModified      TEXT,
            DateDeleted       TEXT,
            IsDeleted         INTEGER NOT NULL DEFAULT 0,
            CHECK (
                (LineType = 'Quote'          AND QuoteId          IS NOT NULL AND StageDirectionId IS NULL AND SoundCueId IS NULL) OR
                (LineType = 'StageDirection' AND StageDirectionId IS NOT NULL AND QuoteId          IS NULL AND SoundCueId IS NULL) OR
                (LineType = 'SoundCue'       AND SoundCueId       IS NOT NULL AND QuoteId          IS NULL AND StageDirectionId IS NULL)
            ),
            UNIQUE (ConversationId, [Order])
        );

        CREATE INDEX IF NOT EXISTS IX_Quotinator_ConversationLine_ConversationId   ON Quotinator_ConversationLine(ConversationId);
        CREATE INDEX IF NOT EXISTS IX_Quotinator_ConversationLine_QuoteId          ON Quotinator_ConversationLine(QuoteId);
        CREATE INDEX IF NOT EXISTS IX_Quotinator_ConversationLine_StageDirectionId ON Quotinator_ConversationLine(StageDirectionId);
        CREATE INDEX IF NOT EXISTS IX_Quotinator_ConversationLine_SoundCueId       ON Quotinator_ConversationLine(SoundCueId);
        CREATE INDEX IF NOT EXISTS IX_Quotinator_StageDirectionTranslation_StageDirectionId ON Quotinator_StageDirectionTranslation(StageDirectionId);
        CREATE INDEX IF NOT EXISTS IX_Quotinator_SoundCueTranslation_SoundCueId    ON Quotinator_SoundCueTranslation(SoundCueId);
        CREATE INDEX IF NOT EXISTS IX_Quotinator_CharacterSource_CharacterId ON Quotinator_CharacterSource(CharacterId);
        CREATE INDEX IF NOT EXISTS IX_Quotinator_CharacterSource_SourceId    ON Quotinator_CharacterSource(SourceId);
        CREATE INDEX IF NOT EXISTS IX_Quotinator_Series_UniverseId           ON Quotinator_Series(UniverseId);
        CREATE INDEX IF NOT EXISTS IX_Quotinator_Source_SeriesId             ON Quotinator_Source(SeriesId);
        """;
}

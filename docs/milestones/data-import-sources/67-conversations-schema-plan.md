# #67 — Conversations schema

**Status:** Planning
**GitHub issue:** #67
**Tiers required:** T1, T2
**Depends on:** #58
**Unblocks:** #68, #69

---

## Scope changes

Cross-checked against `docs/architecture-decisions/` and `docs/database-conventions.md` before
planning (per `process.md`'s mandatory pre-implementation check). The issue as filed on 2026-06-16
predates ADR 002 (RecordBase on all tables, accepted 2026-06-20) and ADR 008 (enum-backed columns
require a CHECK, accepted 2026-07-07). Three corrections to the literal issue spec, all governed by
an existing ADR rather than a new judgment call:

1. **`ConversationLines` gains RecordBase columns** (`DateCreated`, `DateModified`, `DateDeleted`,
   `IsDeleted`). The issue spec omitted them. ADR 002 applies to junction/line tables "without
   exception" — the exact case this table is.
2. **`StageDirectionTranslations`/`SoundCueTranslations` gain a synthetic `Id` and RecordBase
   columns**, replacing the composite-PK-only shape in the issue spec. Every existing translation
   table (`QuoteTranslations`, `SourceTranslations`, `CharacterTranslations`) already uses a
   synthetic `Id` + RecordBase + a `UNIQUE` constraint on the natural key
   (`(EntityId, Language)`) — this brings the two new translation tables in line with that
   established pattern instead of introducing a third, inconsistent shape.
3. **`ConversationLines.LineType` is backed by a real C# enum** (`ConversationLineType`), following
   the `SafeValue<TEnum?>` + `RegisterEnumHandler<TEnum>()` pattern already used for
   `ImportBatchType`/`ImportBatchStatus` — not a plain `string` property. The SQL `CHECK` in the
   issue spec is kept (see Design section 3 below) and split so the simple enum-membership check
   required by ADR 008 is explicit and separate from the FK-exclusivity business rule.

The issue's own "Dependencies" section lists "#67 (curated JSON format)" as a dependency of
itself — a numbering typo for #68 (curated JSON format is #68; the API is #69). Corrected in this
plan doc's header; not otherwise significant.

None of these change the tables' purpose or the data they hold — they bring the shape in line with
conventions the issue predates. No comment posted on the GitHub issue for this; the correction is
recorded here and will be summarised in the issue-closing comment per `checklist.md`.

---

## Spec requirements (as corrected)

Group quotes into ordered conversations, with optional non-quote lines (stage directions, sound
cues) to preserve context and comedic/dramatic timing. A quote can belong to multiple conversations
(different scene variants). A stage direction or sound cue can be reused across conversations and is
translated once.

### `Conversations`
`Id`, `Description` (nullable), `ImportBatchId` (nullable FK → `ImportBatches`), RecordBase.

### `ConversationLines`
`Id`, `ConversationId` (FK → `Conversations`), `Order` (int), `LineType` (enum: `Quote` |
`StageDirection` | `SoundCue`), `QuoteId`/`StageDirectionId`/`SoundCueId` (nullable FKs, exactly one
populated per `LineType`), RecordBase. `UNIQUE (ConversationId, Order)`.

### `StageDirections` / `StageDirectionTranslations`
Scene-setting/action text, optional `ImageUrl`. Translations keyed by `(StageDirectionId,
Language)`.

### `SoundCues` / `SoundCueTranslations`
Audio-cue text, optional `SoundFileUrl`/`ImageUrl`. Translations keyed by `(SoundCueId, Language)`.

Indexes on every FK column.

---

## Design

### 1. Migration

**Status:** ⬜ Not started

New `Migration008_Conversations` appended to `QuotinatorMigrations.All` (`Quotinator.Engine` —
these are Quotinator domain tables, not `Quotinator.Data` infrastructure, per
`docs/database-conventions.md` → "Quotinator.Data must stay domain-agnostic"). One migration, six
tables plus their indexes, since they are introduced together and have FK dependencies on each
other (`ConversationLines` → `Conversations`/`StageDirections`/`SoundCues`).

```sql
CREATE TABLE IF NOT EXISTS Conversations (
    Id            TEXT    PRIMARY KEY,
    Description   TEXT,
    ImportBatchId TEXT    REFERENCES ImportBatches(Id),
    DateCreated   TEXT    NOT NULL,
    DateModified  TEXT,
    DateDeleted   TEXT,
    IsDeleted     INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS StageDirections (
    Id            TEXT    PRIMARY KEY,
    Text          TEXT    NOT NULL,
    ImageUrl      TEXT,
    ImportBatchId TEXT    REFERENCES ImportBatches(Id),
    DateCreated   TEXT    NOT NULL,
    DateModified  TEXT,
    DateDeleted   TEXT,
    IsDeleted     INTEGER NOT NULL DEFAULT 0
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
    Id            TEXT    PRIMARY KEY,
    Text          TEXT    NOT NULL,
    SoundFileUrl  TEXT,
    ImageUrl      TEXT,
    ImportBatchId TEXT    REFERENCES ImportBatches(Id),
    DateCreated   TEXT    NOT NULL,
    DateModified  TEXT,
    DateDeleted   TEXT,
    IsDeleted     INTEGER NOT NULL DEFAULT 0
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
```

`BaselineSchema` in `QuotinatorMigrations.cs` updated in the same commit to include these tables,
per the mandatory baseline/incremental-replay parity rule.

### 2. Entities

**Status:** ⬜ Not started

New `Quotinator.Engine/Entities/` classes, each `RecordBase`-derived with `[Table("...")]`, mirroring
`QuoteTranslationEntity`'s shape: `ConversationEntity`, `ConversationLineEntity`,
`StageDirectionEntity`, `StageDirectionTranslationEntity`, `SoundCueEntity`,
`SoundCueTranslationEntity`. Plus `ConversationLineType` enum (`Quotinator.Engine/Entities/`,
alongside `ImportBatchType`/`ImportBatchStatus`): `Quote`, `StageDirection`, `SoundCue`.
`ConversationLineEntity.LineType` typed as `SafeValue<ConversationLineType?>`, handler registered
once via `RegisterEnumHandler<ConversationLineType>()` in `QuotinatorDapperConfiguration.Configure()`.

### 3. Repositories

**Status:** ⬜ Not started

Standard `IRepository<T>`/`IRestorableRepository<T>` via the generic repository pattern (#71) — no
bespoke Dapper code needed for basic CRUD. `ConversationLines` reads (ordered line list for a given
`ConversationId`) go through a dedicated join query in `Quotinator.Data.Queries.Sql` per the SQL
centralisation policy, consumed by #69's endpoint — not built in this issue, but the query shape
should be designed here since it drives the FK/index choices above. Confirmed the four FK indexes
above are sufficient for that join (each side of the polymorphic FK, plus the parent lookup).

### 4. SQL query guard coverage

**Status:** ⬜ Not started

Any new `Sql.*` factory method added for the join query in section 3 needs a
`SqlQueryGuardTests.AssembledQueryCases` entry per the SQL centralisation policy — even though the
join itself is consumed by #69, the query constant is added here since it's schema-adjacent.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | All six tables created with correct columns, FKs, and RecordBase columns | Unit test | `DatabaseInitializerTests` — new test asserting `PRAGMA table_info` for each table includes `DateCreated`, `DateModified`, `DateDeleted`, `IsDeleted` |
| 2 | ⬜ | `ConversationLines.LineType` CHECK rejects a value outside `Quote`/`StageDirection`/`SoundCue` | Unit test | New test: insert with `LineType = 'Bogus'` throws `SqliteException` |
| 3 | ⬜ | `ConversationLines` FK-exclusivity CHECK rejects mismatched `LineType`/FK combinations | Unit test | New test: insert `LineType = 'Quote'` with `StageDirectionId` set (or `QuoteId` null) throws `SqliteException` |
| 4 | ⬜ | `UNIQUE (ConversationId, Order)` enforced | Unit test | New test: two lines with the same `(ConversationId, Order)` throws `SqliteException` |
| 5 | ⬜ | Translation tables enforce `UNIQUE (EntityId, Language)` | Unit test | New tests for `StageDirectionTranslations` and `SoundCueTranslations` |
| 6 | ⬜ | Baseline schema and incremental replay produce an identical schema | Unit test | `DatabaseInitializerTests.Baseline_And_IncrementalReplay_ProduceIdenticalEngineSchema` |
| 7 | ⬜ | Baseline and incremental replay accept the same CHECK values | Unit test | `DatabaseInitializerTests.Baseline_And_IncrementalReplay_AcceptSameCheckConstraintValues` |
| 8 | ⬜ | `ConversationLineType` round-trips correctly through Dapper (no enum-as-int bug) | Unit test | New `SafeEnumHandler`-pattern test, mirroring existing `ImportBatchType`/`Status` handler tests |
| 9 | ⬜ | App starts in Visual Studio with the new migration applied, no startup error | Live (T1) | Run `dotnet run --project src/Quotinator.Api`; confirm `Data:` startup log line and no exception |
| 10 | ⬜ | Docker build succeeds with the new schema | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` |

---

## Notes

`Conversations` has no `QuoteId` — unlike the stale draft this plan doc replaced, a quote can belong
to zero, one, or many conversations via `ConversationLines`, and `ConversationLines` is the only
place a `QuoteId`/`StageDirectionId`/`SoundCueId` is recorded. This matches the current GitHub issue
spec (updated since the original stub was written) and is what makes the "same quote, different
scene cut" case in the issue's Purpose section representable.

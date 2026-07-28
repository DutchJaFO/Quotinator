# #67 — Conversations schema

**Status:** Waiting for release
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

**Status:** ✅ Done

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

**Status:** ✅ Done

New `Quotinator.Engine/Entities/` classes, each `RecordBase`-derived with `[Table("...")]`, mirroring
`QuoteTranslationEntity`'s shape: `ConversationEntity`, `ConversationLineEntity`,
`StageDirectionEntity`, `StageDirectionTranslationEntity`, `SoundCueEntity`,
`SoundCueTranslationEntity`. Plus `ConversationLineType` enum (`Quotinator.Engine/Entities/`,
alongside `ImportBatchType`/`ImportBatchStatus`): `Quote`, `StageDirection`, `SoundCue`.
`ConversationLineEntity.LineType` typed as `SafeValue<ConversationLineType?>`, handler registered
once via `RegisterEnumHandler<ConversationLineType>()` in `QuotinatorDapperConfiguration.Configure()`.

### 3. Repositories

**Status:** ✅ Done — none registered, by design

Checked `Program.cs` before implementing: `IRestorableRepository<T>` is registered today only for
`QuoteEntity`/`Source`/`Character`/`Person`, and the registration comment says explicitly why —
"needed only by batch-undo (reversal) — nothing else in the app soft-deletes these tables today."
`QuoteTranslations`/`QuoteGenres` (the closest existing precedent for a detail/translation table)
have **no** registered repository at all; they're written via direct SQL in `QuoteSeedWriter`. #67
has no reversal/undo requirement and no writer yet (that's #68), so registering a repository now
would be speculative. None of the six new entities get a DI repository registration in this issue —
one gets added later only if and when a consumer (e.g. a future admin/undo feature) actually needs
it, mirroring the existing precedent exactly rather than pattern-matching "entities generally get
repositories."

### 4. Join query for ordered line reads

**Status:** Deferred to #69

Originally scoped as "design the query shape here, build it in #69" — on reflection this added
nothing #69 can't decide for itself once it has an actual consumer (`QuoteResponse.Conversations`
and `GET /conversations/{id}`) to shape the query against. No `Sql.cs` changes in this issue. The
four `ConversationLines` FK indexes added in section 1 (`ConversationId`, `QuoteId`,
`StageDirectionId`, `SoundCueId`) are sufficient for that future join either direction.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | All six tables created with RecordBase columns | Unit test | `DatabaseInitializerTests.ConversationTables_AllHaveRecordBaseColumns` |
| 2 | ✅ | `ConversationLines.LineType` CHECK rejects a value outside `Quote`/`StageDirection`/`SoundCue` | Unit test | `DatabaseInitializerTests.Baseline_And_IncrementalReplay_AcceptSameCheckConstraintValues` |
| 3 | ✅ | `ConversationLines` FK-exclusivity CHECK rejects mismatched `LineType`/FK combinations | Unit test | `DatabaseInitializerTests.Baseline_And_IncrementalReplay_AcceptSameCheckConstraintValues` |
| 4 | ✅ | `UNIQUE (ConversationId, Order)` enforced | Unit test | `DatabaseInitializerTests.ConversationLines_UniqueConstraint_RejectsDuplicateOrder` |
| 5 | ✅ | Translation tables enforce `UNIQUE (EntityId, Language)` | Unit test | `DatabaseInitializerTests.TranslationTables_UniqueConstraint_RejectsDuplicateLanguage` |
| 6 | ✅ | Baseline schema and incremental replay produce an identical schema, including the six new tables | Unit test | `DatabaseInitializerTests.Baseline_And_IncrementalReplay_ProduceIdenticalEngineSchema` (`EngineDomainTables` extended with the six new table names) |
| 7 | ✅ | Baseline and incremental replay accept the same CHECK values | Unit test | `DatabaseInitializerTests.Baseline_And_IncrementalReplay_AcceptSameCheckConstraintValues` |
| 8 | ✅ | `ConversationLineType` round-trips correctly through Dapper (no enum-as-int bug) | Unit test | `DatabaseInitializerTests.ConversationLineType_RoundTripsThroughDapper` |
| 9 | ✅ | App starts with the new migration applied, no startup error — both the incremental path (existing dev DB) and the fresh baseline path | Live (T1) | Confirmed twice: `dotnet run --project src/Quotinator.Api --configuration Release` (AI session) and by the developer running the solution in Visual Studio (Debug build) — both show `applying 1 pending App migration(s) (version 7 → 8)` then `schema updated (data v9, app v8)`, no exception, app serves `/api/v1/quotes/random` successfully afterward |
| 10 | ✅ | Docker build succeeds with the new schema; fresh container also takes the baseline path cleanly | Live (T2) | `docker build -f docker/Dockerfile -t quotinator:local .` succeeded; `docker run` + `curl /api/v1/version` returned `"schemaVersion":8`; container log shows `fresh database detected — creating schema directly at baseline (data v9, app v8)`, no errors |

All existing tests that hardcoded the previous schema version (`7`) were updated to `8`:
`DatabaseInitializerTests` (4 assertions) and `ImportBatchesTests.Schema_MigrationVersion_IsBumped`.
Full solution: `dotnet build --configuration Release` and `dotnet test --configuration Release`
both clean — 0 warnings, 0 errors, all tests passing (Engine: 125/125; full solution: every project
green).

---

## Notes

`Conversations` has no `QuoteId` — unlike the stale draft this plan doc replaced, a quote can belong
to zero, one, or many conversations via `ConversationLines`, and `ConversationLines` is the only
place a `QuoteId`/`StageDirectionId`/`SoundCueId` is recorded. This matches the current GitHub issue
spec (updated since the original stub was written) and is what makes the "same quote, different
scene cut" case in the issue's Purpose section representable.

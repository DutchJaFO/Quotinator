# #179 — Series/Universe schema: link related Sources, and Character↔Source many-to-many identity

**Status:** Planning
**GitHub issue:** #179
**Tiers required:** T1, T2
**Depends on:** none technically, but corrects #174's original approach and #174 now depends on this issue

---

## Spec requirements (from the GitHub issue)

1. Add a `Universe` table (`Id`, `Name` — unique) and a `Series` table (`Id`, `Name`, `UniverseId`
   nullable FK to `Universe`). One-to-many at both levels: a `Series` belongs to at most one
   `Universe`; a `Source` belongs to at most one `Series`.
2. Add `Source.SeriesId` (nullable FK to `Series`). A `Source` with no `SeriesId` is implicitly
   standalone.
3. Add a `CharacterSources` join table (`CharacterId`, `SourceId`), replacing
   `Characters.SourceId`'s current single required FK.
4. Migrate existing data with **zero merging**: every existing `Characters` row gets exactly one
   `CharacterSources` row carrying its current `SourceId`. Then drop `Characters.SourceId` and its
   `UNIQUE (SourceId, Name)` constraint (rebuild-under-temporary-name pattern). No new uniqueness
   constraint added to `Characters` by this issue.
5. Write an ADR documenting the structural decision: hierarchy shape, `CharacterSources` join, and
   `Source.Type`-as-identity-anchor invariant — not the merge algorithm itself (that's #174's own,
   separate ADR).
6. Update the fresh-database baseline schema in the same commit; add a schema-drift test.
7. Migration policy: numbered, append-only (`Migration009_...`), idempotent where possible, no
   editing of any migration already applied to a real database.

---

## Background — why this issue exists

Filed while researching #169 ("universe/setting" concept). #174 originally planned to make Character
a global entity by copying Person's shape exactly (drop `SourceId`, merge every row sharing a `Name`
globally, no safeguard). #169's research found this concretely wrong: the bundled dataset already
contains real franchises (Lord of the Rings, The Hobbit, Star Wars, Terminator) where a character
spans multiple Sources (Gandalf across six films — Character needs a many-to-many relationship, not
a Source-less global row), and the same Name can validly refer to different portrayals across media
(`Source.Type` must anchor identity — book-Gandalf ≠ film-Gandalf).

This issue lands the structural pieces only. It does **not** merge, consolidate, or delete any
existing `Characters` row, and does **not** decide the merge algorithm — both are #174's job. Every
existing Character row is reshaped 1:1 (its current `SourceId` becomes one `CharacterSources` row) —
this is a pure shape change with zero data-loss or mis-conflation risk of its own, which is what lets
this issue be scoped independently of #174's harder algorithm question.

Populating `Series`/`Universe` values on existing Sources is explicitly **not** this issue's scope,
and needs no new mechanism at all — a hand-authored curated overlay file (same pattern as
`data/sources/quotinator-curated.json`), imported alongside the bundled sources on every startup, can
set `SeriesId` on existing Source ids via #162's already-shipped Source Modify/decidability path.
Being persistent rather than regenerated from upstream, it survives `Quotinator__AutoUpdateSources`
refreshes automatically. An early idea to generalize #153's declarative rule-file mechanism to cover
this was considered and explicitly rejected as overcomplicating a problem the curated-overlay pattern
already solves — #153 was left untouched. Filed separately as **#180** ("Populate Series/Universe
data via curated overlay file (review-only, staged)"), mirroring this milestone's `#67` → `#68`
schema-then-population precedent — #180 must use `duplicateResolution: review` for its own manifest
entry (not the bundled-seeding default of `skip`), so any genuine conflict against an already-curated
value stages for a human decision instead of resolving silently.

---

## Steps

### 1. Write the ADR documenting the structural decision

**Status:** Not started.

Location: `docs/architecture-decisions/NNN-<short-title>.md`, next sequential number after the
current highest (010 as of this writing — confirm the actual next-free number at implementation
time, since sibling in-flight issues may have claimed it first). Standard structure per
`docs/architecture-decisions/README.md`: Status / Date / Context / Decision / Consequences.

Content to settle (all already effectively decided by this planning pass and #169's research —
this step formalises it, it does not reopen it):

- The `Universe` → `Series` → `Source` hierarchy, one-to-many at both levels, not many-to-many —
  reasoned in #169's plan doc step 1 (Simplicity priority; no genuine one-Source-to-many-Series case
  identified).
- `Character` ↔ `Source` becomes many-to-many via a new `CharacterSources` join table, replacing the
  single required `SourceId` FK.
- `Source.Type` is a hard identity anchor: two Character rows must never be merged if their linked
  Sources disagree on `Type`. Stated here as an invariant for #174's own (separate) merge-algorithm
  ADR to operate within — this ADR does not decide how or when #174 applies it.
- This issue's own migration performs zero merging — explicitly stated as a design choice (keeps
  this issue's own risk profile at zero), not an oversight.

### 2. Write the red tests

**Status:** Not started.

At minimum the four listed in the GitHub issue (`Migration_SeriesUniverseSchema_
AddsUniverseAndSeriesTables`, `Migration_SeriesUniverseSchema_PopulatesCharacterSources1to1From
ExistingSourceId`, `Migration_SeriesUniverseSchema_DropsCharactersSourceIdColumn`, `Baseline_And_
IncrementalReplay_ProduceIdenticalSeriesUniverseSchema`), confirmed red against pre-fix code.
Additional coverage to consider once implementation starts: a test asserting the migration performs
literally zero row consolidation (row-count-before equals `CharacterSources`-row-count-after, not
`Characters`-row-count-after, since `Characters` itself is untouched in row count by this issue).

### 3. Design and write the migration

**Status:** Not started.

New entry at the end of `QuotinatorMigrations.All` (`src/Quotinator.Engine/Database/
QuotinatorMigrations.cs`) — currently 8 entries (`Migration001_InitialSchema` through
`Migration008_Conversations`), so this becomes `Migration009_...`.

Sequence within the migration:
1. `CREATE TABLE IF NOT EXISTS Universe (Id TEXT PRIMARY KEY, Name TEXT NOT NULL UNIQUE, ...)` —
   include `RecordBase`-equivalent audit columns per this project's schema convention (check
   `Sources`/`Characters`' own column set for the exact audit-column shape to mirror:
   `ImportBatchId`, `DateCreated`, `DateModified`, `DateDeleted`, `IsDeleted`, `CompletenessStatus`,
   `NoValueKnown` — confirm whether `Universe`/`Series` need the full `RecordBase` shape or a lighter
   one, since neither is directly imported from a source file the way Source/Character/Person are;
   this is a design question for step 1's ADR to settle explicitly, not assumed here).
2. `CREATE TABLE IF NOT EXISTS Series (Id TEXT PRIMARY KEY, Name TEXT NOT NULL UNIQUE, UniverseId
   TEXT NULL REFERENCES Universe(Id), ...)`.
3. `CREATE TABLE IF NOT EXISTS CharacterSources (CharacterId TEXT NOT NULL REFERENCES
   Characters(Id), SourceId TEXT NOT NULL REFERENCES Sources(Id), PRIMARY KEY (CharacterId,
   SourceId))`.
4. `INSERT INTO CharacterSources (CharacterId, SourceId) SELECT Id, SourceId FROM Characters WHERE
   IsDeleted = 0` (or without the `IsDeleted` filter — confirm whether soft-deleted Characters should
   still get a `CharacterSources` row; likely yes, to preserve reversibility, matching how soft
   deletes are handled elsewhere in this codebase — flagged as a decision for implementation time,
   not assumed here).
5. Add `Source.SeriesId TEXT NULL REFERENCES Series(Id)` via `ALTER TABLE Sources ADD COLUMN
   SeriesId TEXT NULL REFERENCES Series(Id)` (SQLite's `ADD COLUMN` has no `IF NOT EXISTS` form —
   per this project's migration policy, this is acceptable since a migration only ever runs once per
   database, tracked via `System_ConsumerSchemaVersion`; not a reason to avoid it, just a reminder it
   cannot be blindly re-run).
6. Drop `Characters.SourceId` and its `UNIQUE (SourceId, Name)` constraint using the
   rebuild-under-temporary-name pattern (`Characters_New` created without `SourceId`/without the old
   `UNIQUE` constraint and without any new one either, data copied via `INSERT INTO Characters_New
   SELECT Id, Name, ImportBatchId, DateCreated, DateModified, DateDeleted, IsDeleted,
   CompletenessStatus, NoValueKnown FROM Characters`, old table dropped, new table renamed) — see
   `Migration004_ImportBatchTypeUserSeed` for the worked example of this exact pattern.

Order matters: step 4 (populate `CharacterSources`) must run **before** step 6 (drop
`Characters.SourceId`), since step 4 reads the column step 6 removes.

### 4. Update the fresh-database baseline and schema-drift test

**Status:** Not started.

`QuotinatorMigrations.BaselineSchema`'s `Characters` table definition (`QuotinatorMigrations.cs:
439-452`) currently carries `SourceId TEXT NOT NULL REFERENCES Sources(Id)` and `UNIQUE (SourceId,
Name)` — both removed in the baseline to match this migration's final shape. `Universe`, `Series`,
`CharacterSources` tables added to the baseline, and `Sources`' own baseline definition gains
`SeriesId`. New `Baseline_And_IncrementalReplay_ProduceIdenticalSeriesUniverseSchema` test (or
equivalent addition to the existing `Baseline_And_IncrementalReplay_ProduceIdenticalEngineSchema`
family in `Quotinator.Engine.Tests`) — a fresh database created via the baseline path must produce a
`CharacterSources` row for every `Characters` row that would exist after a from-empty incremental
seed too (i.e. the equivalence check must cover data shape, not only DDL shape, given this migration
has a data-population step, not just DDL — see #174's own Notes on why a from-empty schema-drift
test alone doesn't catch data-migration classes of bug; this issue's own migration has a much smaller
data-migration surface than #174's will, but the same category of check still applies).

### 5. Audit call sites reading `Character.SourceId` (structural-shape half only)

**Status:** Not started. This issue's own audit is narrower than #174's — it only needs to keep the
codebase *compiling and passing existing tests* against the new `CharacterSources` shape, not decide
any new identity/merge behaviour (that's #174's job). Confirmed-so-far call sites:

- `Sql.Characters.SelectIdBySourceAndName` (`Sql.cs:199-200`) — the query itself still works as a
  read against the old column shape only if `SourceId` still exists; once step 3's migration drops
  it, this query must be rewritten to join through `CharacterSources` instead
  (`SELECT c.Id FROM Characters c JOIN CharacterSources cs ON cs.CharacterId = c.Id WHERE
  cs.SourceId = @sourceId AND c.Name = @name AND c.IsDeleted = 0`), preserving today's exact
  matching behaviour (still effectively per-Source until #174 changes the matching key) — this
  issue keeps `ResolveCharacterAsync`'s current per-Source lookup behaviour unchanged in *meaning*,
  only in *mechanism* (query now goes through the join table). #174 is the one that changes the
  actual matching key to something Source-independent.
- `Sql.Characters.InsertIfNotExists` (`Sql.cs:213-215`) — drops `SourceId` from the `INSERT`
  statement's column list; the corresponding `CharacterSources` row insert is a separate statement
  added alongside it (both must succeed together — same transaction).
- `Sql.Characters.CountActiveReferences` and `Sql.Sources.CountActiveReferences` (`Sql.cs:264-266`)
  — `Sources.CountActiveReferences`'s Character-counting half currently assumes `Characters.SourceId`
  as a column; rewritten to join through `CharacterSources`. Behaviour preserved (a Source is still
  "referenced" by every Character currently linked to it) — no semantic change, just a mechanism
  change, per this issue's own scope boundary.
- `CharacterActionPayload` (`ImportActionPlanner.cs:507`) and `ResolveCharacterAsync`
  (`ImportActionPlanner.cs:212-246`) — **left alone by this issue**, deliberately. Both still operate
  in terms of a single `SourceId` per Character, since this issue does not change the *matching* key
  or the *payload* shape — only the underlying storage mechanism. #174 is where these actually change
  to reflect global, many-to-many identity.
- `Quotinator.slnx` — this plan doc, the ADR, and any new Sql factory/const entries created by this
  issue are added in the same commit that creates them.

### 6. Documentation

**Status:** Not started. `README.md`/`addon/DOCS.md` are unaffected unless this issue adds a new
endpoint (it doesn't — pure schema). No action expected here beyond confirming that at
implementation time.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | Structural decision (hierarchy shape, `CharacterSources` join, `Source.Type` anchor invariant) documented | Doc | New ADR under `docs/architecture-decisions/` — exact number/filename not yet known |
| 2 | ❌ | `Universe`/`Series` tables added; `Source.SeriesId` added | Unit test | `Quotinator.Engine.Tests.Migration_SeriesUniverseSchema_AddsUniverseAndSeriesTables` — starts red |
| 3 | ❌ | Every existing `Characters` row gets exactly one `CharacterSources` row from its current `SourceId`, zero merging | Unit test | `Quotinator.Engine.Tests.Migration_SeriesUniverseSchema_PopulatesCharacterSources1to1FromExistingSourceId` — starts red |
| 4 | ❌ | `Characters.SourceId` and its old `UNIQUE (SourceId, Name)` constraint are dropped | Unit test | `Quotinator.Engine.Tests.Migration_SeriesUniverseSchema_DropsCharactersSourceIdColumn` — starts red |
| 5 | ❌ | Fresh-database baseline and incremental replay produce an identical schema, including `CharacterSources` data shape | Unit test | `Quotinator.Engine.Tests.Baseline_And_IncrementalReplay_ProduceIdenticalSeriesUniverseSchema` — starts red |
| 6 | ❌ | Existing Character lookup/insert/reference-count behaviour is unchanged in meaning after the mechanism change | Unit test | Existing `ImportActionPlanner`/`SqliteImportActionService` Character tests still pass unmodified in behaviour (mechanism-only change) |
| 7 | ❌ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green, 0 warnings, 0 errors |
| 8 | ❌ | Migration applies cleanly against a database matching the last published release's schema, not just from-empty | Live (T1) | Per ADR 009: reconstruct or check out the last released tag's schema, run this migration against it, confirm no drift; developer confirms app opens and runs correctly in Visual Studio afterward |
| 9 | ❌ | Live import behaviour is unchanged post-migration (no new duplicates, no lost Character-Source links) | Live (T2) | Docker smoke test against `docker build -f docker/Dockerfile -t quotinator:local .` — import a quote referencing an existing Character/Source pair, confirm behaviour matches pre-migration (via `Quotinator.Tools.DbInspector` inspecting `CharacterSources`) |

---

## Notes

T1 and T2 are both required — this issue touches migration SQL and schema-rebuild logic (ADR 009 /
`docs/release-verification.md`'s explicit T1 criterion), independent of this project's blanket
T1/T2 rule.

This issue's own migration is deliberately merge-free and risk-free by design — the harder, genuinely
open question (the actual Character merge algorithm, using `Source.Type` and `Series`/`Universe`
scoping to consolidate rows safely) belongs entirely to #174, which depends on this issue landing
first. See `174-character-global-identity-plan.md` for that work.

Populating `Series`/`Universe` data on existing Sources is out of scope for this issue and needs no
new mechanism — see Background above and #180. Do not add a new "enrichment rule" concept to #153 for
this; that idea was raised and explicitly rejected as overcomplicating a problem the curated-overlay-
file pattern (reusing #162's Modify path) already solves.

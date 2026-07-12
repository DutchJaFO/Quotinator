# #174 — Character: from per-Source to global identity (ADR + migration)

**Status:** Planning
**GitHub issue:** #174
**Tiers required:** T1, T2
**Depends on:** none technically, but sequenced after #173 (Person) so it can reuse Person's proven global-entity shape

---

## Spec requirements (from the GitHub issue)

1. Decide the merge algorithm for consolidating existing per-source `Character` rows that share a
   `Name` into one global row — including what happens to divergent `CompletenessStatus`/
   `NoValueKnown` values across the rows being merged, and whether any safeguard (e.g. requiring a
   "same franchise" signal via #169 if it lands first, or requiring manual confirmation for
   ambiguous merges) is needed to avoid wrongly conflating two unrelated characters who share a
   name. Document the decision and its reasoning in an ADR (`docs/architecture-decisions/`).
2. Design and write the migration: drop `Character.SourceId`'s FK/column (or the equivalent schema
   change the ADR settles on), merge existing rows per the ADR's algorithm, re-point every
   `Quotes.CharacterId` that referenced a merged-away row to its new canonical row. Follow this
   project's migration policy exactly — numbered, append-only, idempotent where possible, no
   editing of any migration already applied to a real database.
3. `EntityIdentity.CharacterId`'s stable-id derivation changes from `(sourceId, name)` to `name`
   alone, matching `EntityIdentity.PersonId`.
4. `ResolveCharacterAsync`'s natural-key lookup (`ImportActionPlanner.cs`) changes from
   `Sql.Characters.SelectIdBySourceAndName` (`sourceId|name`) to a new
   `Sql.Characters.SelectIdByName` (`name` alone), mirroring `Sql.People.SelectIdByName`.
5. Every other call site that currently reads/writes `Character.SourceId` is audited and updated to
   match the new global shape — including `CharacterActionPayload`'s current `SourceId`/
   `SourceTitle`/`SourceType` fields (their post-migration meaning, if any, is part of this issue's
   design work).
6. Update the fresh-database baseline schema in the same commit as the migration; add a
   schema-drift test per the existing baseline/incremental-replay convention.

---

## Steps

### 1. Write the ADR deciding the merge algorithm

**Status:** Not started.

This step's output is the ADR itself — its content is **not** pre-decided by this plan doc, per the
issue's own explicit instruction that the exact merge algorithm "was deliberately not decided
during planning." What the ADR needs to settle, at minimum:

- How existing per-source `Characters` rows sharing the same `Name` are consolidated into one
  global row (case/whitespace normalisation rules, if any — check whether `EntityIdentity`'s
  existing `QuoteIdentity.Normalise` helper, already used by `StableId`, is the right normalisation
  to reuse here for consistency).
- What happens to divergent `CompletenessStatus`/`NoValueKnown` values across the rows being
  merged (e.g. does any `Complete` row's status win, does a conflict fall back to `Incomplete`, is
  a merge blocked entirely when statuses disagree).
- Whether any safeguard is needed against wrongly conflating two unrelated characters that happen
  to share a name (e.g. two different "Sarah"s, two different "The Doctor"s in unrelated
  properties) — options the issue itself raises include requiring a "same franchise" signal (see
  #169, "universe/setting" research — read its findings if #169 lands first, but this issue is not
  blocked on it) or requiring manual confirmation for ambiguous merges. Whether such a safeguard is
  adopted, deferred, or rejected is the ADR's decision to make and record, not this plan doc's.
- What `CharacterActionPayload`'s current `SourceId`/`SourceTitle`/`SourceType` fields mean, if
  anything, after Character becomes global (see step 5 below).

Format and location: `docs/architecture-decisions/NNN-<short-title>.md`, next sequential number
after the current highest (010 as of this writing — confirm the actual next-free number at
implementation time, since sibling in-flight issues may have claimed it first), following this
project's standard ADR structure (`docs/architecture-decisions/README.md`): **Status** (Proposed /
Accepted / Superseded / Deprecated) / **Date** / **Context** / **Decision** / **Consequences**,
header fields stating the current fact only, never an accumulated history (per the README's own
rule and `ADR 002`'s worked example).

### 2. Write the red tests

**Status:** Not started. Cannot be written until step 1's ADR settles the exact merge algorithm —
the "Expected tests" table in the GitHub issue is a starting point, not exhaustive (the issue says
so explicitly), and its coverage will expand once the algorithm is known. At minimum the five
listed tests (`Migration_CharacterGlobalIdentity_MergesSameNameRowsAcrossSources`,
`Migration_CharacterGlobalIdentity_RepointsQuoteCharacterIdToMergedRow`,
`Migration_CharacterGlobalIdentity_PreservesCompletenessStatusPerAlgorithm`,
`Baseline_And_IncrementalReplay_ProduceIdenticalCharactersSchema`,
`ResolveCharacterAsync_ExistingGlobalCharacter_ReusesRealId`) confirmed red against pre-fix code.

### 3. Design and write the migration

**Status:** Not started.

Added as a new entry at the end of `QuotinatorMigrations.All`
(`src/Quotinator.Engine/Database/QuotinatorMigrations.cs`) — currently 8 entries
(`Migration001_InitialSchema` through `Migration008_Conversations`), so this becomes
`Migration009_...` — never editing any existing entry, per this project's append-only migration
policy.

This migration is more involved than a typical DDL-only entry in this codebase: it must both
change the schema (drop `Characters.SourceId`, and its `UNIQUE (SourceId, Name)` constraint, which
becomes `UNIQUE (Name)` to match `People.Name`'s own `NOT NULL UNIQUE` shape) *and* merge/re-point
existing data in the same pass, per whatever algorithm the ADR (step 1) settles on. Since SQLite
cannot alter a `UNIQUE` constraint or drop a column that participates in one in place, this follows
the existing "rebuild under a temporary name, copy data, drop, rename" pattern already used in this
codebase for a comparable constraint change — see `Migration004_ImportBatchTypeUserSeed` for the
worked example (`ImportBatches_New` created, data copied, old table dropped, new table renamed).
The data-merge step (consolidating rows and re-pointing `Quotes.CharacterId` to the surviving row)
has to happen as part of, or immediately alongside, that rebuild — every DDL statement must stay
idempotent where SQLite allows it (`CREATE TABLE IF NOT EXISTS`), but the merge/re-point DML itself
needs its own idempotency reasoning since a partial failure part-way through a data migration is a
different risk profile than a partial failure part-way through pure DDL (see `docs/database-conventions.md`'s
Migrations table and ADR 009 for why a from-empty schema-drift test alone won't catch this class of
issue — it never exercises the merge logic against pre-existing rows at all).

`Characters.SourceId` also participates in `Sql.Characters.CountActiveReferences` (used by
`Sources.CountActiveReferences`, which sums Quotes and Characters referencing a Source) and
`Sql.Sources.CountActiveReferences` — both must be reviewed for whether they still make sense once
a Character is no longer scoped to one Source (see step 5's audit list).

### 4. `EntityIdentity.CharacterId` and the natural-key lookup

**Status:** Not started.

- `EntityIdentity.CharacterId(string sourceId, string name)`
  (`src/Quotinator.Core/Import/EntityIdentity.cs:19`) changes signature to
  `CharacterId(string name)`, deriving `StableId("character", name)` — matching
  `PersonId(string name) => StableId("person", name)` exactly. Existing stable ids computed under
  the old two-argument form will not match the new one-argument form for any given character name;
  the migration (step 3) is what reconciles already-stored ids with newly-computed ones going
  forward, not this method itself.
- `Sql.Characters.SelectIdBySourceAndName` (`Sql.cs:199-200`) is replaced by a new
  `Sql.Characters.SelectIdByName`, mirroring `Sql.People.SelectIdByName` (`Sql.cs:223`) exactly:
  `SELECT Id FROM Characters WHERE Name = @name AND IsDeleted = 0;`.
- `ResolveCharacterAsync` (`ImportActionPlanner.cs:212-246`) updates its lookup key from
  `$"{sourceId}|{q.Character}"` to `q.Character` alone (mirroring `ResolvePersonAsync`'s
  `index.TryGetValue(q.Author, ...)` pattern exactly), calls the new `SelectIdByName`, and calls
  `EntityIdentity.CharacterId(q.Character)` with the new one-argument signature.
- `Sql.Characters.InsertIfNotExists` (`Sql.cs:213-215`) drops the `SourceId` column from its
  `INSERT OR IGNORE` statement and parameter list.

### 5. Audit every other `Character.SourceId` call site

**Status:** Not started. Confirmed-so-far call sites needing review (final list may grow once step
1's ADR is written and step 3's migration design is finalised):

- `CharacterActionPayload` (`ImportActionPlanner.cs:507`) — currently
  `record CharacterActionPayload(string SourceId, string Name, string SourceTitle, string SourceType)`.
  Its post-migration shape (drop the three Source-related fields entirely, keep them as informational
  metadata only, or something else) is explicitly named in the GitHub issue as part of this issue's
  own design work — not pre-decided here.
- `ImportActionPlanner.ResolveCharacterAsync`'s construction of `CharacterActionPayload(sourceId, q.Character, q.Source, sourceTypeStr)`
  (`ImportActionPlanner.cs:240`) and its doc-comment at `ImportActionPlanner.cs:504-506` explicitly
  citing `Characters.SourceId` as "a real foreign key" — both need updating together.
- `SqliteImportActionService.cs:519-524`'s `Character` apply-time branch, whose own comment notes
  `Characters.SourceId is a real FK` and that the action may apply before its Source has — this
  entire ordering rationale needs re-examination once `SourceId` is no longer a column.
- `SqliteImportActionService.cs:571-572` (`EnsureCharacterExistsAsync(..., payload.CharacterId, payload.SourceId, ...)`)
  and its downstream `EnsureCharacterExistsAsync` signature.
- `SqliteImportActionService.cs:827-828`'s `ToFieldMap(CharacterActionPayload payload)`, which maps
  `["sourceId"] = payload.SourceId` into the decide-time field-merge vocabulary — remove or repurpose
  in step with whatever `CharacterActionPayload`'s new shape becomes.
- `Sql.Characters.CountActiveReferences` and `Sql.Sources.CountActiveReferences`
  (`Sql.cs:264-266`) — the latter's own comment explicitly reasons about "a Character can outlive
  the specific Quote that introduced it," a rationale tied to per-Source scoping that needs
  re-reading once Character is global.
- `Quotinator.slnx` is **not** touched by this step — no new files are added by the audit itself,
  only edits to existing ones; any new file created in steps 1–3 (the ADR, the new migration
  constant, new Sql factory/const entries) is added to the slnx in the same commit that creates it,
  per this project's normal file-placement convention, not deferred to a separate pass.

### 6. Update the fresh-database baseline and schema-drift test

**Status:** Not started. `QuotinatorMigrations.BaselineSchema`'s own `Characters` table definition
(`QuotinatorMigrations.cs:439-452`) currently duplicates `Migration001_InitialSchema`'s shape plus
the `ImportBatchId`/`CompletenessStatus`/`NoValueKnown` columns added by later migrations, and still
carries `SourceId TEXT NOT NULL REFERENCES Sources(Id)` and `UNIQUE (SourceId, Name)`. Per this
project's standing rule (CLAUDE.md's "Baseline schema for fresh databases," restated in
`docs/database-conventions.md`'s Migrations table), this baseline must be updated in the *same
commit* as the new migration to produce an identical final shape — enforced by a new
`Baseline_And_IncrementalReplay_ProduceIdenticalCharactersSchema` test (or equivalent addition to
the existing `Baseline_And_IncrementalReplay_ProduceIdenticalEngineSchema` family in
`Quotinator.Engine.Tests`), following the same pattern as the existing schema-drift tests for other
tables.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | Merge algorithm and its data-loss/conflation risk profile are decided and documented | Doc | New ADR under `docs/architecture-decisions/` — exact number/filename not yet known |
| 2 | ❌ | Existing per-source rows sharing a `Name` are merged into one global row, per the ADR's algorithm | Unit test | `Quotinator.Engine.Tests.Migration_CharacterGlobalIdentity_MergesSameNameRowsAcrossSources` — starts red |
| 3 | ❌ | Every `Quotes.CharacterId` referencing a merged-away row is re-pointed to the surviving row | Unit test | `Quotinator.Engine.Tests.Migration_CharacterGlobalIdentity_RepointsQuoteCharacterIdToMergedRow` — starts red |
| 4 | ❌ | Divergent `CompletenessStatus`/`NoValueKnown` values across merged rows are resolved per the ADR's algorithm | Unit test | `Quotinator.Engine.Tests.Migration_CharacterGlobalIdentity_PreservesCompletenessStatusPerAlgorithm` — starts red |
| 5 | ❌ | Fresh-database baseline and incremental replay produce an identical `Characters` schema | Unit test | `Quotinator.Engine.Tests.Baseline_And_IncrementalReplay_ProduceIdenticalCharactersSchema` — starts red |
| 6 | ❌ | `ResolveCharacterAsync` reuses an existing global Character by name alone, independent of Source | Unit test | `Quotinator.Engine.Tests.ResolveCharacterAsync_ExistingGlobalCharacter_ReusesRealId` — starts red |
| 7 | ❌ | This list expands once the merge algorithm (row 1) is decided — the five tests above are a starting point, not exhaustive, per the GitHub issue's own caveat | — | Reassess after step 1 (ADR) and step 2 (red tests) are complete |
| 8 | ❌ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green, 0 warnings, 0 errors |
| 9 | ❌ | Migration applies cleanly against a database matching the last published release's schema, not just from-empty | Live (T1) | Per ADR 009: reconstruct or check out the last released tag's schema, run this migration against it, confirm no drift from the from-empty incremental/baseline result; developer confirms app opens and runs correctly in Visual Studio afterward |
| 10 | ❌ | Live import behaviour is correct post-migration: importing a quote whose Character name already exists globally (under a different Source) reuses the existing row instead of creating a duplicate | Live (T2) | Docker smoke test against `docker build -f docker/Dockerfile -t quotinator:local .` — import two quotes with the same Character name under two different Sources, confirm only one `Characters` row is created and both quotes reference it |

---

## Notes

T1 and T2 are both required. T1 specifically because this issue touches `DatabaseInitializer`/
`QuotinatorDatabaseInitializer`-equivalent migration SQL and schema-rebuild logic — one of
`docs/release-verification.md`'s explicit T1 "When required" criteria — not only because of this
project's blanket T1/T2 rule (per the direct developer correction recorded against #168). Given
that this migration also merges and re-points existing data (not pure DDL), ADR 009's requirement
to verify the incremental migration path against a database matching the **last published
release's** schema — not just the accumulated local dev database or the from-empty schema-drift
tests — applies with particular weight here; see step 3 and verification row 9.

The exact merge algorithm and its data-loss/conflation risk profile are **not** decided by this
plan doc — they are this issue's own first deliverable (the ADR, step 1). A related research issue
(#169, "universe/setting" concept) may inform the algorithm if it lands first, but this issue is
not blocked waiting for it, per the GitHub issue's own text.

Current-code findings from this planning pass that sharpen (without changing) the issue's stated
risk profile:

- `Characters` currently has a real `UNIQUE (SourceId, Name)` constraint (not just an FK) — both the
  original `Migration001_InitialSchema` and the current `BaselineSchema` enforce it. Dropping
  `SourceId` therefore isn't only an FK removal; it also requires rebuilding the `UNIQUE` constraint
  itself down to `Name` alone (mirroring `People.Name`'s `NOT NULL UNIQUE`), which needs the
  rebuild-under-temporary-name pattern this codebase already uses for `Migration004_ImportBatchTypeUserSeed`
  (documented in `docs/database-conventions.md`'s Migrations table) rather than a simple `ALTER TABLE
  ... DROP COLUMN`.
- `Sql.Sources.CountActiveReferences` already sums both `Quotes` and `Characters` referencing a
  Source, with an existing comment reasoning that "a Character can outlive the specific Quote that
  introduced it" — this reference-counting logic is built directly on `Characters.SourceId` existing
  as a column and will need re-examination (not just a rename) once Character has no Source scope at
  all, which the GitHub issue's own item 5 anticipates but does not itemise this specific query.
- `CharacterActionPayload` and its `ToFieldMap` mapping in `SqliteImportActionService.cs` currently
  feed `sourceId` into the decide-time field-merge vocabulary (`FieldMergeResolver`) as a real
  mergeable field, not just a display value — so the ADR's decision on what `SourceId`/`SourceTitle`/
  `SourceType` mean post-migration also determines whether decide-time field merging for Character
  loses a field entirely, which is a slightly larger blast radius than "drop a column."

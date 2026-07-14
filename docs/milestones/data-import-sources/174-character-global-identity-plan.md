# #174 — Character: migrate to global identity via new Series/Universe schema (ADR + migration)

**Status:** Planning
**GitHub issue:** #174
**Tiers required:** T1, T2
**Depends on:** #179 (Series/Universe schema, `CharacterSources` join, `Source.Type` anchor invariant)

---

## Spec requirements (from the GitHub issue)

1. Decide the merge algorithm for consolidating existing per-source `Character` rows that share a
   `Name` into fewer global rows, operating within #179's structural boundary (`Source.Type` is a
   hard anchor; a `Series`/`Universe` link, where known, scopes a safer cross-Source merge) —
   including what happens to divergent `CompletenessStatus`/`NoValueKnown` values, and the
   conservative-by-default behaviour when no Series relationship is known between two Sources.
   Document the decision in an ADR — scoped to the algorithm only; #179's own ADR covers the
   structural shape.
2. Design and write the migration: consolidate `CharacterSources`-linked rows per the ADR's
   algorithm, re-point every `Quotes.CharacterId` that referenced a merged-away row to its new
   canonical row, establish whatever uniqueness constraint the ADR's merge key implies. Depends on
   #179's migration having landed first.
3. `EntityIdentity.CharacterId`'s stable-id derivation changes from `(sourceId, name)` to whatever
   key this issue's ADR settles on (likely `name` plus a `Type`-derived component).
4. `ResolveCharacterAsync`'s natural-key lookup changes to query through `CharacterSources` with the
   new key, rather than a `Characters.SourceId` column (#179 already moved the query mechanism to
   `CharacterSources`, keeping the old per-Source *meaning*; this issue changes the *meaning*).
5. Every other call site that currently reads/writes `Character.SourceId`/`CharacterActionPayload`'s
   Source-related fields is audited and updated to match the new merge behaviour.
6. Update the fresh-database baseline schema in the same commit as the migration; add a
   schema-drift test.

---

## Background — corrected scope (2026-07-14)

This issue originally planned to copy Person's shape exactly: drop `Character.SourceId` entirely and
merge every row sharing a `Name` into one global row, accepting the collision risk with no safeguard.
**That framing was corrected during #169's research** (see #169's closing comment and
`169-universe-setting-research-plan.md`): the bundled dataset already contains real franchises (Lord
of the Rings, The Hobbit, Star Wars, Terminator) where a character spans multiple Source rows
(Gandalf across six films — Character needs a many-to-many relationship to Source, not a Source-less
global row), and the same Name can validly refer to different portrayals across different media
(`Source.Type` must anchor identity — a book adaptation's Gandalf and a film adaptation's Gandalf are
different Characters despite sharing a Name and a Universe). Merging by Name alone would not just be
risky — it would be concretely wrong given data already bundled with this project.

**This issue now depends on #179** ("Series/Universe schema: link related Sources, and
Character↔Source many-to-many identity"), which lands the structural pieces this issue needs: the
`Universe`→`Series`→`Source` hierarchy, the `CharacterSources` many-to-many join, and the
`Source.Type`-as-identity-anchor invariant. #179 performs zero data merging of its own — every
existing `Characters` row keeps its own `SourceId`, 1:1, via `CharacterSources`. **This issue is
where the actual data consolidation and merge algorithm live.**

Since Series/Universe data will likely be sparse or unpopulated at first (population happens
gradually via a curated overlay file — see #179's Background — not as part of this issue), the
algorithm should default conservative: with no known Series relationship between two Sources, this
issue's migration should not auto-merge their same-named Characters. The initial migration may
therefore consolidate little to nothing beyond what's already been explicitly curated — an
intentional, safe starting point, not a shortfall.

This issue does **not** add Modify/decidability to Character — it only lands the migration/identity
change. A separate follow-on issue (#175) builds Modify on top of the new global model, using
Person's (#173) proven shape as a starting template, adjusted for Character's many-to-many Source
relationship.

---

## Steps

### 1. Write the ADR deciding the merge algorithm

**Status:** Not started. Blocked on #179 landing (needs `CharacterSources`, `Series`, `Universe` to
exist before this ADR can reference them concretely).

This step's output is the ADR itself — its content is **not** pre-decided by this plan doc, per the
issue's own explicit instruction that the exact merge algorithm "was deliberately not decided during
planning." What the ADR needs to settle, at minimum:

- The exact merge key: `Name` alone is insufficient (per #169's finding) — most likely `Name` plus
  a `Source.Type`-derived component (e.g. two Characters are merge-candidates only if every Source
  either already links to one of them, or shares the same `Type` as every Source already linked to
  it). The precise algorithm for evaluating this across an arbitrary number of already-linked
  Sources per Character is this ADR's core decision.
- How a `Series`/`Universe` relationship (where known via `Source.SeriesId`/`Series.UniverseId`)
  scopes a safer cross-Source merge than "same Name, same Type, no other signal" — and the explicit
  fallback behaviour when no Series relationship is known: conservative-by-default, do not merge.
- What happens to divergent `CompletenessStatus`/`NoValueKnown` values across the rows being merged
  (e.g. does any `Complete` row's status win, does a conflict fall back to `Incomplete`).
- What `CharacterActionPayload`'s current `SourceId`/`SourceTitle`/`SourceType` fields mean, if
  anything, once Character is genuinely many-to-many with Source (see step 5 below) — #179
  deliberately left these untouched; this issue's ADR is where their post-merge shape is decided.
- What uniqueness constraint (if any) `Characters` gains once the merge key is known — #179
  deliberately left `Characters` without one, since it didn't know the key.

Format and location: `docs/architecture-decisions/NNN-<short-title>.md`, next sequential number
after the current highest at implementation time (011 or later, depending on whether #179's own ADR
has already claimed the next slot — #179 is sequenced before this issue, so its ADR number should
already exist by the time this step starts).

### 2. Write the red tests

**Status:** Not started. Cannot be written until step 1's ADR settles the exact merge algorithm — the
"Expected tests" table in the GitHub issue is a starting point, not exhaustive. At minimum the seven
listed tests (`Migration_CharacterMerge_ConsolidatesSameNameRowsWithinKnownSeries`,
`Migration_CharacterMerge_NeverMergesAcrossDifferingSourceType`, `Migration_CharacterMerge_
LeavesUnrelatedSameNameRowsUnmergedWhenNoSeriesKnown`, `Migration_CharacterMerge_
RepointsQuoteCharacterIdToMergedRow`, `Migration_CharacterMerge_PreservesCompletenessStatusPer
Algorithm`, `Baseline_And_IncrementalReplay_ProduceIdenticalCharactersSchema`,
`ResolveCharacterAsync_ExistingGlobalCharacter_ReusesRealId`) confirmed red against pre-fix code.
The `NeverMergesAcrossDifferingSourceType` and `LeavesUnrelatedSameNameRowsUnmergedWhenNoSeriesKnown`
tests are the direct regression guards for #169's corrected findings — both must exist, not just the
positive-merge cases.

### 3. Design and write the migration

**Status:** Not started. Depends on #179's migration (`Migration009_...`) having landed — this
becomes `Migration010_...` or later, whatever is next-free at implementation time.

Unlike #179's own migration (zero merging, pure shape change), this migration performs real data
consolidation: for each group of Characters sharing a `Name` (and satisfying the ADR's `Type`/
`Series` conditions from step 1), pick a canonical surviving row, re-point every `CharacterSources`
row and every `Quotes.CharacterId` that referenced a merged-away row to the survivor, then soft- or
hard-delete the merged-away rows (per whichever this codebase's existing merge precedent uses —
check how #59's admin soft-reset or #162's duplicate-resolution policies handle an analogous
"multiple rows collapse to one" case, rather than inventing a new deletion convention here).

This is a genuinely data-migration-shaped step, not pure DDL — per ADR 009 and
`docs/database-conventions.md`'s Migrations table, a from-empty schema-drift test alone will not
catch bugs in this class of migration; verification row 9 (T1 against a database matching the last
published release's schema) carries particular weight here, same reasoning already established for
#179's own Notes section.

Establishing `Characters`' new uniqueness constraint (deferred by #179) happens here, once the merge
key is known — likely `UNIQUE (Name, <Type-derived component>)`, using the same
rebuild-under-temporary-name pattern #179 already used once in this migration chain.

### 4. `EntityIdentity.CharacterId` and the natural-key lookup

**Status:** Not started.

- `EntityIdentity.CharacterId(string sourceId, string name)` (`src/Quotinator.Core/Import/
  EntityIdentity.cs:19`) changes signature to match whatever key step 1's ADR settles on — likely
  `CharacterId(string name, string sourceType)` rather than `CharacterId(string name)` alone (unlike
  `PersonId`, which has no anchor to consider). Existing stable ids computed under the old
  two-argument `(sourceId, name)` form will not match the new form; the migration (step 3) reconciles
  already-stored ids with newly-computed ones.
- `Sql.Characters.SelectIdBySourceAndName` — already rewritten by #179 to join through
  `CharacterSources` while preserving old per-Source *meaning*. This step changes the query's
  *meaning* to the new merge key (e.g. matching on `Name` + the `Type` of every currently-linked
  Source, not a single `SourceId` parameter).
- `ResolveCharacterAsync` (`ImportActionPlanner.cs:212-246`) updates its lookup key from
  `$"{sourceId}|{q.Character}"` to whatever step 1 decides, and calls the new
  `EntityIdentity.CharacterId` signature.
- `Sql.Characters.InsertIfNotExists` — already updated by #179 to insert into both `Characters` and
  `CharacterSources`; this step doesn't change its column list further, only the value computed for
  `Id` (via the new `EntityIdentity.CharacterId` signature).

### 5. Audit every other `Character.SourceId`/`CharacterActionPayload` call site

**Status:** Not started. #179 already handled the pure mechanism-level call sites (queries now go
through `CharacterSources`); this step is about *behaviour*, not mechanism:

- `CharacterActionPayload` (`ImportActionPlanner.cs:507`) — currently
  `record CharacterActionPayload(string SourceId, string Name, string SourceTitle, string SourceType)`.
  Its post-merge shape (drop the three Source-related fields entirely, keep them as informational
  metadata only, or represent "every currently-linked Source" as a collection) is this issue's own
  design work.
- `ImportActionPlanner.ResolveCharacterAsync`'s construction of `CharacterActionPayload(...)` and its
  doc-comment explicitly citing `Characters.SourceId` as "a real foreign key" — both need updating to
  reflect the many-to-many reality.
- `SqliteImportActionService.cs`'s `Character` apply-time branch, whose comment notes
  `Characters.SourceId is a real FK` and reasons about ordering relative to when its Source applies —
  this ordering rationale needs re-examination now that a Character can reference multiple Sources,
  potentially applying at different times across a batch.
- `SqliteImportActionService.cs`'s `EnsureCharacterExistsAsync(..., payload.CharacterId,
  payload.SourceId, ...)` and its downstream signature — updated to add a `CharacterSources` link
  rather than set a single `SourceId`.
- `SqliteImportActionService.cs`'s `ToFieldMap(CharacterActionPayload payload)`, which maps
  `["sourceId"] = payload.SourceId` into the decide-time field-merge vocabulary — removed or
  repurposed per step 1's ADR decision on what `CharacterActionPayload`'s new shape is.
- `Sql.Characters.CountActiveReferences`/`Sql.Sources.CountActiveReferences` — #179 already updated
  these to join through `CharacterSources` with unchanged meaning; no further change expected here
  unless the ADR's merge changes what "actively referenced" should mean for a many-to-many Character.

### 6. Update the fresh-database baseline and schema-drift test

**Status:** Not started. `QuotinatorMigrations.BaselineSchema`'s `Characters` table definition,
already updated once by #179 (SourceId/old UNIQUE removed), gains whatever new uniqueness constraint
step 3 establishes. New or extended schema-drift test confirming baseline and incremental replay
agree, including the merge behaviour's resulting row counts for a fixture containing a known
multi-Source Character group.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | Merge algorithm, its `Type`-anchor enforcement, and its conservative-by-default fallback are decided and documented | Doc | New ADR under `docs/architecture-decisions/` — exact number/filename not yet known |
| 2 | ❌ | Characters sharing a `Name` are merged into one row only when the ADR's Series/Type conditions are satisfied | Unit test | `Quotinator.Engine.Tests.Migration_CharacterMerge_ConsolidatesSameNameRowsWithinKnownSeries` — starts red |
| 3 | ❌ | Two Characters are never merged if their linked Sources disagree on `Type` | Unit test | `Quotinator.Engine.Tests.Migration_CharacterMerge_NeverMergesAcrossDifferingSourceType` — starts red |
| 4 | ❌ | Two same-named Characters with no known Series relationship are left unmerged (conservative default) | Unit test | `Quotinator.Engine.Tests.Migration_CharacterMerge_LeavesUnrelatedSameNameRowsUnmergedWhenNoSeriesKnown` — starts red |
| 5 | ❌ | Every `Quotes.CharacterId` referencing a merged-away row is re-pointed to the surviving row | Unit test | `Quotinator.Engine.Tests.Migration_CharacterMerge_RepointsQuoteCharacterIdToMergedRow` — starts red |
| 6 | ❌ | Divergent `CompletenessStatus`/`NoValueKnown` values across merged rows are resolved per the ADR's algorithm | Unit test | `Quotinator.Engine.Tests.Migration_CharacterMerge_PreservesCompletenessStatusPerAlgorithm` — starts red |
| 7 | ❌ | Fresh-database baseline and incremental replay produce an identical `Characters` schema | Unit test | `Quotinator.Engine.Tests.Baseline_And_IncrementalReplay_ProduceIdenticalCharactersSchema` — starts red |
| 8 | ❌ | `ResolveCharacterAsync` reuses an existing global Character by the new merge key | Unit test | `Quotinator.Engine.Tests.ResolveCharacterAsync_ExistingGlobalCharacter_ReusesRealId` — starts red |
| 9 | ❌ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green, 0 warnings, 0 errors |
| 10 | ❌ | Migration applies cleanly against a database matching the last published release's schema, not just from-empty | Live (T1) | Per ADR 009: reconstruct or check out the last released tag's schema, run this migration against it, confirm no drift; developer confirms app opens and runs correctly in Visual Studio afterward |
| 11 | ❌ | Live import behaviour is correct post-migration: importing a quote whose Character name already exists globally under a Source of the *same* `Type` and known `Series` reuses the existing row; a differing `Type` never merges | Live (T2) | Docker smoke test — import two quotes with the same Character name under two Sources of differing `Type`, confirm two separate `Characters` rows persist; repeat with matching `Type` and a shared `Series`, confirm one row |

---

## Notes

T1 and T2 are both required. T1 specifically because this issue touches migration SQL and
schema-rebuild logic (ADR 009 / `docs/release-verification.md`'s explicit T1 criterion). Given this
migration merges and re-points existing data (not pure DDL), ADR 009's requirement to verify the
incremental migration path against a database matching the last published release's schema applies
with particular weight here.

The exact merge algorithm is **not** decided by this plan doc — it is this issue's own first
deliverable (the ADR, step 1), operating within #179's structural boundary. See #179's plan doc for
the schema/concept work this issue depends on, and #169's plan doc/closing comment for the corrected
research findings that reshaped this issue's scope on 2026-07-14.

**Not this issue's concern, but relevant context for whoever picks up #175 next:** #173 (Person)
found that its `_personRepository`-based Add-reversal and stale-Add-cleanup code paths were both on
the Guid-typed repository API, which silently no-ops against a lowercase, file-authored explicit id
(`GuidHandler` force-uppercases before comparing). This issue doesn't introduce an explicit Character
id itself, so it isn't exposed — but #175 (which does) inherits the identical exposure at the
identical two call sites, and its own plan doc has been updated accordingly (see
`175-character-modify-plan.md`'s steps 8/9). No action needed here.

# #174 — Character: migrate to global identity via new Series/Universe schema (ADR + migration)

**Status:** Released
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

**Status:** ✅ Done (2026-07-24). Written as `docs/architecture-decisions/
013-character-merge-algorithm.md`, registered in `Quotinator.slnx`. Settles: the merge-candidate test
(Name exact match + `Source.Type` anchor + a known, shared, non-null `SeriesId` between the candidate's
already-linked Sources and the Source being resolved — Universe-level relationships deliberately
excluded as a merge signal); that the identical test applies both retroactively (the migration) and
prospectively (`ResolveCharacterAsync`, with a documented, accepted same-batch limitation); canonical
survivor selection (earliest `DateCreated`, tie-broken by smallest `Id`); `CompletenessStatus`
resolves to the most-reviewed value across a merged group, `NoValueKnown` to the deduplicated union;
`EntityIdentity.CharacterId`'s new signature is `(sourceId, name, sourceType)` — **not** `(name,
sourceType)` as this plan doc's own earlier speculation floated, which the ADR shows would be a real
correctness bug (see the ADR's Decision 5); `Characters` gains a denormalized `SourceType` column with
a matching `CHECK` but **no** new `UNIQUE` constraint (two independent same-Name-same-Type Characters
can legitimately coexist when no Series connects them); a single unified SQL query replaces
`Sql.Characters.SelectIdBySourceAndName`; and `CharacterActionPayload`/apply-time code need no
functional changes at all (#179 already built the apply-time plumbing generically enough).

### 2. Write the red tests

**Status:** ✅ Done. Written as `Migration_CharacterGlobalIdentity_*` (not `Migration_CharacterMerge_*`
as originally named here, to match the migration's actual class name) in
`DatabaseInitializerTests.cs`: `ConsolidatesSameNameRowsWithinKnownSeries`,
`MergesDespiteDifferingNameCasing`, `NeverMergesAcrossDifferingSourceType`,
`LeavesUnrelatedSameNameRowsUnmergedWhenNoSeriesKnown`, `RepointsQuoteCharacterIdToMergedRow`,
`PreservesCompletenessStatusPerAlgorithm`, `BackfillsSourceTypeColumnFromLinkedSource`. Plus five
`ResolveCharacterAsync_*` tests in `ImportActionPlannerTests.cs` covering the same-Source case,
Series-scoped cross-Source reuse, the Type-anchor block, the conservative no-Series default, and
case-insensitive Name matching. The originally-planned `Baseline_And_IncrementalReplay_
ProduceIdenticalCharactersSchema` and same-batch-limitation tests were not needed as separate tests —
see steps 6 and 8's own notes for why.

### 3. Design and write the migration

**Status:** ✅ Done. Implemented as `Migration011_CharacterGlobalIdentity` (Migration010 was already
claimed by #213 this same milestone). Backfills `Characters.SourceType`, computes merge groups via a
correlated subquery keyed on `(LOWER(Name), SourceType, SeriesId)`, re-points `CharacterSources`/
`Quotes.CharacterId`, resolves `CompletenessStatus` to the most-reviewed value, soft-deletes
merged-away rows.

Unlike #179's own migration (zero merging, pure shape change), this migration performs real data
consolidation: for each group of Characters sharing a `Name` (and satisfying the ADR's `Type`/
`Series` conditions from step 1), pick a canonical surviving row, re-point every `CharacterSources`
row and every `Quotes.CharacterId` that referenced a merged-away row to the survivor, then soft- or
hard-delete the merged-away rows (per whichever this codebase's existing merge precedent uses —
check how #59's admin soft-reset or #162's duplicate-resolution policies handle an analogous
"multiple rows collapse to one" case, rather than inventing a new deletion convention here).

This is a genuinely data-migration-shaped step, not pure DDL — per ADR 009 and
`docs/database-conventions.md`'s Migrations table, a from-empty schema-drift test alone will not
catch bugs in this class of migration; verification row 11 (T1 against a database matching the last
published release's schema) carries particular weight here, same reasoning already established for
#179's own Notes section.

Establishing `Characters`' new uniqueness constraint (deferred by #179) happens here, once the merge
key is known — likely `UNIQUE (Name, <Type-derived component>)`, using the same
rebuild-under-temporary-name pattern #179 already used once in this migration chain.

### 4. `EntityIdentity.CharacterId` and the natural-key lookup

**Status:** ✅ Done. `EntityIdentity.CharacterId`'s new signature is `(sourceId, name, sourceType)` —
**not** `(name, sourceType)` as this section originally speculated; see ADR 013 Decision 5 for why
dropping `sourceId` would have been a real correctness bug. `Sql.Characters.SelectIdBySourceAndName`
was replaced by `Sql.Characters.SelectGlobalCandidateId` (ADR 013 Decision 7), and
`ResolveCharacterAsync` now also reads the resolving Source's `SeriesId`
(`Sql.Sources.SelectSeriesIdById`) as the Series-relatedness signal.

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

**Status:** ✅ Done. ADR 013 Decision 9 concluded `CharacterActionPayload` and the apply-time machinery
need **no functional changes** — #179 already built `EnsureCharacterExistsAsync`/the Quote apply
branch's defensive re-ensure generically enough to support cross-Source reuse for free. The one real
addition: `EnsureCharacterExistsAsync` now also threads `sourceType` through to `Characters.
InsertIfNotExists` (both existing call sites already had a `Type` string in scope). The reversal path
(`SqliteImportActionService.cs`'s re-resolve-from-restored-text logic) was also updated to the new
`SelectGlobalCandidateId` lookup — not originally listed in this section, found while implementing.
Doc-comment sweep completed (`CharacterActionPayload`'s own summary, `Sql.Characters`'s query
comments). #179 already handled the pure mechanism-level call sites (queries now go through
`CharacterSources`); this step was about *behaviour*, not mechanism:

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

**Status:** ✅ Done. `QuotinatorMigrations.BaselineSchema`'s `Characters` table gains `SourceType TEXT
NOT NULL DEFAULT 'Unknown'` with the matching `CHECK` (ADR 008) — **no new `UNIQUE` constraint** (ADR
013 Decision 6 concluded one would be actively wrong, since two independent same-`(Name,SourceType)`
Characters can legitimately coexist when no Series connects them). The pre-existing
`Baseline_And_IncrementalReplay_ProduceIdenticalConsumerSchema` test (`DatabaseInitializerTests.cs`)
already iterates `ConsumerDomainTables`, which already includes `Characters` — no new dedicated
schema-drift test was needed; that existing test provides the coverage this row asks for.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Merge algorithm, its `Type`-anchor enforcement, and its conservative-by-default fallback are decided and documented | Doc | `docs/architecture-decisions/013-character-merge-algorithm.md` |
| 2 | ✅ | Characters sharing a `Name` are merged into one row only when the ADR's Series/Type conditions are satisfied | Unit test | `Quotinator.Core.Tests.Migration_CharacterGlobalIdentity_ConsolidatesSameNameRowsWithinKnownSeries` — passing |
| 3 | ✅ | Two Characters are never merged if their linked Sources disagree on `Type` | Unit test | `Quotinator.Core.Tests.Migration_CharacterGlobalIdentity_NeverMergesAcrossDifferingSourceType` — passing |
| 4 | ✅ | Two same-named Characters with no known Series relationship are left unmerged (conservative default) | Unit test | `Quotinator.Core.Tests.Migration_CharacterGlobalIdentity_LeavesUnrelatedSameNameRowsUnmergedWhenNoSeriesKnown` — passing |
| 5 | ✅ | Every `Quotes.CharacterId` referencing a merged-away row is re-pointed to the surviving row | Unit test | `Quotinator.Core.Tests.Migration_CharacterGlobalIdentity_RepointsQuoteCharacterIdToMergedRow` — passing |
| 6 | ✅ | Divergent `CompletenessStatus`/`NoValueKnown` values across merged rows are resolved per the ADR's algorithm | Unit test | `Quotinator.Core.Tests.Migration_CharacterGlobalIdentity_PreservesCompletenessStatusPerAlgorithm` — passing |
| 7 | ✅ | Name matching is case-insensitive; storage preserves original casing (developer correction, 2026-07-24) | Unit test | `Quotinator.Core.Tests.Migration_CharacterGlobalIdentity_MergesDespiteDifferingNameCasing` — passing |
| 8 | ✅ | Fresh-database baseline and incremental replay produce an identical `Characters` schema | Unit test | `Quotinator.Core.Tests.Baseline_And_IncrementalReplay_ProduceIdenticalConsumerSchema` (pre-existing, iterates `ConsumerDomainTables` which includes `Characters`) — passing |
| 9 | ✅ | `ResolveCharacterAsync` reuses an existing global Character by the new merge key (same-Source and Series-scoped cross-Source) | Unit test | `Quotinator.Core.Tests.ResolveCharacterAsync_ExistingGlobalCharacter_ReusesRealId`, `...SeriesScopedCrossSourceMatch_ReusesExistingCharacter`, `...DifferingSourceType_NeverReusesExistingCharacter`, `...NoKnownSeriesRelationship_CreatesSeparateCharacter`, `...CaseInsensitiveNameMatch_ReusesRealId` — all passing |
| 10 | ✅ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` (2026-07-24) — 2182 tests, all passed, 0 warnings, 0 errors, across every project |
| 11 | ✅ | Migration applies cleanly against a database matching the last published release's schema, not just from-empty | Live (T1) | Developer confirmed (2026-07-24): real dev database at schema v10 migrated cleanly to v11 in Visual Studio, app started, `GET /api/v1/masterdata/characters` returned 200 |
| 12 | ✅ | Live import behaviour is correct post-migration: importing a quote whose Character name already exists globally under a Source of the *same* `Type` and known `Series` reuses the existing row; a differing `Type` never merges | Live (T2) | Docker smoke test (2026-07-24): pre-committed "The Fellowship of the Ring"/"The Two Towers" (same Series, both Movie) each given an "Aragorn174" quote in separate imports → exactly one `Aragorn174` Character row linked to both Sources; identical setup with one Source `Movie` and one `Book` → two separate `Gandalf174` rows despite the shared Series. Found and fixed a real scope gap during this pass — see ADR 013 Decision 8's rewritten text. |

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

**Note found stale during this review (2026-07-24), not corrected here — out of #174's own scope:** the
paragraph below (written 2026-07-16) described a `GuidHandler` bug where it "force-uppercases before
comparing." #210's subsequent work this milestone flipped the system-wide id-casing convention entirely
— `GuidHandler` and every other id-presentation choke point now canonicalize to **lowercase**, not
uppercase (see ADR 012's final "system-wide lowercase" revision). The specific uppercase-comparison
exposure described below no longer exists in that form. Whoever picks up #175 next should re-verify its
own plan doc's steps 8/9 against current `GuidHandler`/`ToCanonicalId()` behaviour before relying on
this now-outdated description — not something to fix as part of #174.

**Original note (superseded, kept for context):** #173 (Person) found that its `_personRepository`-based
Add-reversal and stale-Add-cleanup code paths were both on the Guid-typed repository API, which silently
no-ops against a lowercase, file-authored explicit id (`GuidHandler` force-uppercases before comparing).
This issue doesn't introduce an explicit Character id itself, so it isn't exposed — but #175 (which
does) inherits the identical exposure at the identical two call sites, and its own plan doc has been
updated accordingly (see `175-character-modify-plan.md`'s steps 8/9). No action needed here.

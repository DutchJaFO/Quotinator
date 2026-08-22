# #155 — Migration review: verify full incremental path from last-shipped v1.7.2 schema

**Status:** Released
**GitHub issue:** #155
**Tiers required:** T1, T2
**Depends on:** none (sequenced last in this milestone, per its own issue text and `overview.md`'s dependency map)

---

## Scope revision (2026-07-28, developer direction)

The original plan (see "Original plan (superseded)" below) treated this issue as pure verification:
reconstruct a v1.7.2 snapshot, replay every migration this milestone added, and confirm the result
matches the from-empty tests. That verification pass — done directly against `main` and the current
feature branch as part of writing this revision — found a genuine, currently-shipping-if-released
bug (see "Bug found" below), and the developer set an additional, previously unstated goal:

> An unstated goal is that ideally we should have only one migration left to get from the 1.7.2
> release to where we are now. The only issues/migrations that got published are those that are
> currently in the main branch as released.

**This changes the issue from "verify the incremental path" to "verify, then collapse it."** Since
nothing this milestone added has ever shipped in a real release, every migration added since v1.7.2
is safe to rewrite, reorder within its own unreleased span, or squash — the project's "never edit an
already-applied migration" rule protects migrations that have reached a *real installation*, and none
of these have. Confirmed directly against `main` (`git show main:...`), not assumed: `main` still
uses the pre-#143 single-project, single-counter migration model (`Quotinator.Engine`, one
`QuotinatorMigrations.All` list, one `SchemaVersion` table) with exactly **4** migrations applied —
`Migration001_InitialSchema`, `Migration002_ReseedGenres`, `Migration003_ImportBatches`,
`AuditMigrations.CreateAuditEntriesTable`. Everything past that point — the entire migration-ownership
split (#143), every Data-owned migration, every Consumer-owned migration from `ImportBatchTypeUserSeed`
onward — exists only on this feature branch and has never reached a published release.

## Bug found: legacy `SchemaVersion` rename silently skips Data migrations 2-4 on a real upgrade

`DatabaseInitializer.RenameLegacySchemaVersionTableIfPresentAsync` detects a real v1.7.2 database's
legacy, unified `SchemaVersion` table and renames it directly to `System_SchemaVersion` (Data's new
counter) via a bare `ALTER TABLE ... RENAME TO` — **the stored `Version` value is never adjusted**.

A real v1.7.2 database's `SchemaVersion` table holds `MAX(Version) = 4` (one row per migration
actually applied, confirmed against `main`'s own migration-application code, which inserts one row
per version exactly like the current code does). After the bare rename, `GetDataCurrentVersion`
reads this same value — `4` — directly into `dataCurrent`. But Data's own migration list is numbered
independently, starting fresh at 1, and only Data migration **1** (`AuditMigrations
.CreateAuditEntriesTable`) is the *same* SQL as the old unified migration 4. Data migrations 2, 3,
and 4 today are `RenameAuditEntriesToSystemAuditEntries`, `CreateImportConflictsTable`, and
`CreateChangeLogTable` — three migrations that **never ran** on a real v1.7.2 database. Because
`dataCurrent = 4 ≥ 4`, the replay loop (`Version > dataCurrent` only) treats all four as
"already applied" and skips straight to migration 5. On a genuine v1.7.2 → current upgrade, this
means:

- `AuditEntries` is never renamed to `System_AuditEntries`
- `System_ImportConflicts` is never created
- `System_ChangeLog` is never created

...while `DataSchemaVersion` still reports "13, fully up to date" once migrations 5-13 finish
running. Every runtime read/write against `System_AuditEntries`/`System_ImportConflicts`/
`System_ChangeLog` would then throw "no such table" — a real, currently-shipping-if-released,
completely silent data-loss/crash bug. It has never been caught because every existing test that
exercises this path (`DatabaseInitializerOwnershipTests.cs`, and the #143 plan doc's own
`DowngradeToLegacyNamesAsync` notes) deliberately overrides the legacy row to `Version = 1` first —
a convenient value for testing Data's own migration 2 in isolation, not the value a genuine v1.7.2
database actually has. This is exactly the class of bug ADR 009/#155 exist to catch, and this is the
first time the check has actually been run against `main`'s real code instead of an assumed or
locally-convenient legacy state.

## Adjusted decision (confirmed with developer)

1. **Fix the legacy-transition bug at the root**, by replacing the bare rename with an explicit,
   correct split: detect the legacy `SchemaVersion` table, insert its rows for old versions 1-3 into
   `System_ConsumerSchemaVersion` unchanged (`InitialSchema`/`ReseedGenres`/`ImportBatches` — genuinely
   Consumer-owned, matching today's Consumer migrations 1-3 exactly), insert its row for old version 4
   into `System_SchemaVersion` **renumbered to 1** (`CreateAuditEntriesTable` — genuinely Data-owned
   migration 1), preserving each row's original `AppliedAt` timestamp, then drop the legacy table.
   Never copy the legacy table's raw version number directly into either new counter again.
2. **Squash Consumer migrations 4-11** (`ImportBatchTypeUserSeed` through `CharacterGlobalIdentity` —
   8 migrations, everything added since v1.7.2) **into one new Consumer migration 4**.
3. **Squash Data migrations 2-13** (`RenameAuditEntriesToSystemAuditEntries` through
   `CreateSourceFileOverridesTable` — 12 migrations, everything added since v1.7.2) **into one new
   Data migration 2**.

Net result: a real v1.7.2 database reaches current with exactly **one** new Data migration and
**one** new Consumer migration — the smallest number possible given this architecture's two
independently-tracked counters (Data-owned vs. Consumer-owned, per CLAUDE.md's "Migration ownership
split" section — that split itself stays; only the *count of migrations since v1.7.2* is collapsed).
The fresh-install baseline path (`DataBaselineSql`/`QuotinatorMigrations.Baseline`) is unaffected by
any of this — it already reaches final schema in one step for a genuinely empty database; this
consolidation only changes what the *incremental* (non-empty starting state) path looks like.

**Original plan (superseded):** the plan as originally written assumed the milestone's remaining
issues (#170-#176) hadn't landed yet and treated this as a pure audit with no code changes beyond
possibly a test fixture. Both premises are now stale — every other issue in this milestone has landed,
and the audit itself surfaced a real bug plus the consolidation goal above. Requirement 5 from the
original GitHub issue text (decide on a permanent v1.7.2 fixture) is retained below as its own step.

---

## Full inventory of what's being consolidated

**Consumer-owned, unreleased (squash into new migration 4):**
| Old version | Constant | What it does |
|---|---|---|
| 4 | `Migration004_ImportBatchTypeUserSeed` | Adds `UserSeed` to `ImportBatches.Type` CHECK |
| 5 | `Migration005_ImportBatchConflictPolicy` | Adds `ImportBatches.ConflictPolicy` |
| 6 | `Migration006_RecordCompleteness` | Adds `CompletenessStatus`/`NoValueKnown` to Quotes/Sources/Characters/People |
| 7 | `Migration007_ImportBatchStagingStatus` | Adds `ImportBatches.Status`/`AppliedAt` for staged batches |
| 8 | `Migration008_Conversations` | Adds `Conversations`/`ConversationLines`/`StageDirections`/`SoundCues` + translation tables |
| 9 | `Migration009_SeriesUniverseSchema` | Adds `Series`/`Universe`/`CharacterSources`, drops `Characters.SourceId`, adds `Sources.SeriesId` |
| 10 | `Migration010_RenameImportBatchImportedById` | Renames `ImportBatches.ImportedBy` → `ImportedById` |
| 11 | `Migration011_CharacterGlobalIdentity` | Character global-identity retrofit (#174) |

**Data-owned, unreleased (squash into new migration 2):**
| Old version | Constant | What it does |
|---|---|---|
| 2 | `AuditMigrations.RenameAuditEntriesToSystemAuditEntries` | Renames `AuditEntries` → `System_AuditEntries` |
| 3 | `ImportConflictMigrations.CreateImportConflictsTable` | Creates `System_ImportConflicts` |
| 4 | `ChangeLogMigrations.CreateChangeLogTable` | Creates `System_ChangeLog` |
| 5 | `AuditMigrations.MigrateToRecordBase` | Retrofits `System_AuditEntries` onto `RecordBase` |
| 6 | `ImportConflictMigrations.MigrateToRecordBase` | Retrofits `System_ImportConflicts` onto `RecordBase` |
| 7 | `ImportConflictMigrations.AddExistingBatchId` | Adds `System_ImportConflicts.ExistingBatchId` |
| 8 | `ImportActionMigrations.CreateImportActionsTable` | Creates `System_ImportActions` |
| 9 | `ImportConflictMigrations.AddStatusCheckConstraint` | Widens `System_ImportConflicts.Status` CHECK |
| 10 | `ImportActionMigrations.AddBlockedStatusAndMarkCompletenessAs` | Adds `Blocked` status + `MarkCompletenessAs` |
| 11 | `ImportActionMigrations.AddOriginalDecision` | Adds `System_ImportActions.OriginalDecision` |
| 12 | `ImportActionMigrations.AddStaleStatus` | Adds `Stale` status |
| 13 | `SourceFileOverrideMigrations.CreateSourceFileOverridesTable` | Creates `System_SourceFileOverrides` |

**Staying exactly as-is (already released in v1.7.2, frozen forever):**
- Consumer 1-3: `Migration001_InitialSchema`, `Migration002_ReseedGenres`, `Migration003_ImportBatches`
- Data 1: `AuditMigrations.CreateAuditEntriesTable` (renumbered from old unified migration 4, content unchanged)

---

## Steps

### 1. Fix the legacy `SchemaVersion` split (the bug)

**Status:** Done, implemented 2026-07-28.

Replace `RenameLegacySchemaVersionTableIfPresentAsync` with explicit split logic:
```sql
INSERT INTO System_ConsumerSchemaVersion (Version, AppliedAt)
  SELECT Version, AppliedAt FROM SchemaVersion WHERE Version IN (1, 2, 3);
INSERT INTO System_SchemaVersion (Version, AppliedAt)
  SELECT 1, AppliedAt FROM SchemaVersion WHERE Version = 4;
DROP TABLE SchemaVersion;
```
Reorder `ApplyMigrationsAsync` so the `isEmptyDatabase` check (`AnyTableExists`) still runs *before*
either version table is created (unchanged requirement), then create both version tables, then run
the split (needs both tables to already exist as insert targets). New `Sql.Schema` constants:
`SplitLegacySchemaVersionIntoConsumer`, `SplitLegacySchemaVersionIntoData`,
`DropLegacySchemaVersionTable` — replacing `RenameLegacySchemaVersionTable`.

**Tests to add**: a version of `DatabaseInitializerOwnershipTests`/`DatabaseInitializerTests` that
seeds a genuine 4-row legacy `SchemaVersion` table (versions 1-4, matching what `main` actually
produces) rather than the existing tests' convenience `Version = 1` override, and confirms: (a)
`System_ConsumerSchemaVersion` ends up with exactly rows 1-3, (b) `System_SchemaVersion` ends up with
exactly row 1 (not 4), (c) `System_AuditEntries`/`System_ImportConflicts`/`System_ChangeLog` all
exist and are queryable afterward (the actual symptom the bug produces), (d) original `AppliedAt`
timestamps survive the split. Existing tests (`InitialiseAsync_LegacySchemaVersionTable_...`,
`InitialiseAsync_LegacyAuditEntriesTable_...`, `DowngradeToLegacyNamesAsync`) need rewriting to match
the new split semantics instead of the old bare-rename ones.

**Implemented as designed**, plus one test-fixture wrinkle worked through during implementation:
`DatabaseInitializerTests.DowngradeToLegacyNamesAsync` was rewritten to seed the real 4-row legacy
`SchemaVersion` state (with 4 distinct markers — `LegacyV1Marker`..`LegacyV4Marker` — so each row's
destination is individually verifiable), clearing both new counter tables first so the split has a
genuinely empty target. The rewritten
`InitialiseAsync_LegacyV172SchemaVersionTable_SplitsCorrectlyAndReplaysRemainingMigrations` (renamed
from `InitialiseAsync_LegacySchemaVersionTable_IsRenamedWithRowsPreserved`) and the existing
`InitialiseAsync_LegacyAuditEntriesTable_MigratesToSystemAuditEntriesWithRowsPreserved` both build
their test database via a full `InitialiseAsync()` first (so its domain tables are already fully
migrated), then downgrade only the version-counter tables — meaning Consumer's counter correctly
regresses to 3 after the split, but Consumer's own domain tables (already renamed/altered by the
*first* `InitialiseAsync()` call, e.g. `ImportBatches.ImportedById`) no longer match what a genuinely
legacy-shaped v1.7.2 database would have. A real replay of Consumer's migrations 4-11 against them
hits real conflicts (found live: `SQLite Error 1: 'no such column: ImportedBy'`, since migration 10
tries to rename a column that was already renamed by the initial full setup). Both tests pass a
second initializer with an **empty Consumer migration list** to sidestep this — Data's own fixed
migration list still applies unconditionally regardless, so both tests still fully exercise the bug
they guard against (Data's own migrations 2-4 correctly replay from the split's true starting point).
Exercising a genuine end-to-end Consumer replay against a truly legacy-shaped database is step 5's
job (a real `v1.7.2` git-worktree snapshot), not these two unit tests'.

### 2. Squash Consumer migrations 4-11 into one

**Status:** Done, implemented 2026-07-28.

Concatenated the 8 migrations' SQL bodies, in their existing order, into `Migration004_ConsolidatedSinceV172`
— literal text concatenation of `Migration004_ConsolidatedSinceV172Core` (schema-creation portion) plus
`CharacterGlobalIdentityMerge` (the #174/ADR 013 merge logic, kept as its own constant so it stays
independently unit-testable). `QuotinatorMigrations.All` shrinks to 4 entries. The 8 replaced migration
constants and their doc comments are gone from the source; their "why" reasoning was preserved in a
consolidated doc comment on `Migration004_ConsolidatedSinceV172Core` (git history retains the originals;
CLAUDE.md's "never edit an already-applied migration" doesn't apply since none has ever reached a real
release). `Migration001_InitialSchema`/`Migration002_ReseedGenres`/`Migration003_ImportBatches` were
widened from `private` to `internal` — they're frozen forever (confirmed unchanged against `main`), so
tests may execute them directly to build a genuine v1.7.2-shaped fixture.

**Test-fixture rule established mid-implementation (developer direction, verbatim):** "we do not skip
migrations for any reason. testing for specific migrations is a flawed test by default. we either
migrate a database or we do not. should not have test that depends on a specific migration. we test if
the final result has all the features we are testing for — we already test how migrations work." In
practice: no test may pass a truncated migration list (e.g. `QuotinatorMigrations.All.Take(N)`) to
`CreateInitializer`/`DatabaseInitializer` — that fakes what migrations even exist, which no real
deployment ever does. To build old-shape fixture data, execute the real, frozen migration SQL directly
against a raw connection instead, then either (a) run the ordinary, complete, untruncated
`CreateInitializer`/`InitialiseAsync` path, informing it what already genuinely happened via
`System_ConsumerSchemaVersion` rows that record a true fact (see
`ImportBatchesTests.Migration_RenameImportedByToImportedById_ColumnRenamedAndDataPreserved`), or (b) for
`CharacterGlobalIdentityMerge` specifically, invoke the SQL fragment directly as a unit of logic (see
below).

**A correctness-adjacent finding surfaced while fixing these tests, resolved 2026-07-28:** folding
schema-creation and the character-merge logic into one atomic migration means `Sources.SeriesId` is
*always* `NULL` at the exact moment `CharacterGlobalIdentityMerge` runs for any real upgrading user —
the column was just created moments earlier in the same migration, with zero opportunity for the app's
own import/seeding path to have populated it yet. Per ADR 013 Decision 1(c), a Character with no
Series-known linked Source is never merged, so as written the merge sub-logic can never actually merge
anything on any real *incremental* upgrade. `SeedPreMergeCharactersAsync` and
`Migration_CharacterGlobalIdentity_RepointsQuoteCharacterIdToMergedRow` invoke
`CharacterGlobalIdentityMerge` directly (never through `CreateInitializer`) specifically because no real
migration replay can ever reach the precondition they need to unit-test the merge SQL's own logic — this
is a deliberate, narrow exception to the "always run the full untruncated path" rule above, scoped to
this one migration fragment.

**Developer resolution:** accepted, not a bug to fix. The upgrade path from v1.7.2 that this milestone
will actually recommend to users is reset-and-reseed, not an in-place incremental migration — the
bundled data quality has improved enough since v1.7.2 that a full reseed is the intended path regardless.
Series/Universe (#179) is new in this milestone, so no pre-existing v1.7.2 installation could ever have
had Sources.SeriesId populated in the first place; and v1.7.2 itself never shipped an import feature
(imports are also new in this milestone), so there was never a mechanism that could have populated it
even in principle. The merge logic being "migration-time-only, inert on a real incremental upgrade" is
therefore expected and correct given the release strategy, not a gap needing a later re-trigger.

### 3. Squash Data migrations 2-13 into one

**Status:** Done, implemented 2026-07-29.

Added a new `src/Quotinator.Data/Database/DataConsolidatedMigrations.cs` with `SinceV172` —
`DataOwnedMigrations`'s new entry 2. Unlike Consumer's consolidation (Step 2), this is built as a
literal C# const concatenation of the 12 *original, still-existing* constants
(`AuditMigrations.RenameAuditEntriesToSystemAuditEntries + ImportConflictMigrations.CreateImportConflictsTable
+ ...`), not a copy of their SQL text into one new block, and none of the 12 original constants were
deleted — a deliberate deviation from this step's original wording ("delete the 12 now-dead
constants"). Reason found during implementation: `SourceFileOverrideMigrations.CreateSourceFileOverridesTable`
has a real external reference (`SourceFileOverrideRegistryTests.cs` executes it directly to build a
minimal repository-test fixture, unrelated to migration replay) — deleting it would have broken a
legitimate test or forced it to run the full consolidated migration just to get one table. Keeping all
12 as named, independently-referenceable constants and concatenating by reference: (a) preserves that
test unchanged, (b) is a strictly safer technique than hand-copying 12 blocks of SQL text (zero risk of
a transcription error silently changing shipped-adjacent SQL), and (c) each constant remains its own
single source of truth. `DataOwnedMigrations` now has exactly 2 entries (version 1 = frozen
`CreateAuditEntriesTable`, version 2 = `DataConsolidatedMigrations.SinceV172`).

### 4. Update every test/doc that hard-codes the old migration counts or version numbers

**Status:** Done, implemented 2026-07-29 (folded into Steps 2 and 3's own implementation rather than
run as a separate pass — the audit below reflects what was actually found and fixed).

- `DatabaseInitializerOwnershipTests.cs`: `Assert.AreEqual(13, db.DataSchemaVersion/dataRows, ...)` → 2
  (two occurrences). `InitialiseAsync_ExistingDatabaseAtDataVersion9_UpgradesSystemImportActionsWithBlockedAndMarkCompletenessAs`
  was **deleted**, not rewritten — its entire premise (a database recorded at intermediate Data-version
  9) can no longer exist once only versions 1 and 2 remain; there is no "9" to be at anymore. Same class
  of test as the two deleted earlier in Step 2 (arbitrary version pinning with no real-world meaning
  post-consolidation).
- `DatabaseInitializerTests.cs` (Core.Tests): three `Assert.AreEqual(11, ...)` → 4
  (`InitialiseAsync_PartialMigrationState_...`, `InitialiseAsync_TrulyEmptyDatabase_...`,
  `InitialiseAsync_PreSplitCombinedCounterDatabase_...`) and two `Assert.AreEqual(13, db.DataSchemaVersion, ...)`
  → 2, plus one stale comment ("migrations 2-13's own rows") corrected to reflect the single remaining
  migration 2.
- Every `QuotinatorMigrations.All.Take(N)` partial-migration test setup was removed entirely, not
  re-derived for the new 4-entry list — per the developer's explicit rule established during Step 2
  ("we do not skip migrations for any reason"), no test may pass a truncated migration list to
  `CreateInitializer` at all, regardless of what `N` would need to mean. See Step 2's own notes for the
  replacement technique.
- `SqlQueryGuardTests.AggregateQueries_MatchDocumentedInventory`: no hit — none of the consolidated
  constant names appear in its inventory.
- `CLAUDE.md` / `docs/smoke-tests.md` (now `docs/automated-testing/`): no hit — neither hard-codes a specific migration count or
  version number for this migration set.
- ADR 002/004/009/012/013: no hit — none reference a specific migration version number for this set.
- **Historical milestone plan docs under `docs/milestones/` were not touched**, per this project's
  existing convention.

### 5. Reconstruct and verify against the real v1.7.2 snapshot

**Status:** Done, verified 2026-07-29.

`git worktree add <path> v1.7.2` created a real checkout of the last published release (confirmed:
`Quotinator.Engine` still exists as its own project, `QuotinatorMigrations.All` has exactly 4 entries
— `Migration001_InitialSchema`, `Migration002_ReseedGenres`, `Migration003_ImportBatches`,
`AuditMigrations.CreateAuditEntriesTable` — a single unified `SchemaVersion` counter, no #143
Data/Consumer split). A throwaway `[TestMethod]` added to that worktree's own
`Quotinator.Engine.Tests` (never committed — the worktree was deleted afterward) ran the real,
unmodified v1.7.2 `QuotinatorDatabaseInitializer.InitialiseAsync()` against a fresh empty file with
all three bundled source files seeded, producing a genuine v1.7.2-shaped `.db` snapshot.

That snapshot was copied and pointed at the current branch's `CreateInitializer([])` (the ordinary,
untruncated `QuotinatorMigrations.All`, 4 entries) via a second throwaway test in
`DatabaseInitializerTests.cs` (also removed afterward, pending Step 7's decision on whether to keep a
permanent version). Result: **no exception**, `DataSchemaVersion = 2`, `SchemaVersion = 4`,
every expected table present (`System_AuditEntries`, `System_ImportConflicts`, `System_ChangeLog`,
`System_ImportActions`, `System_SourceFileOverrides`, `Conversations`, `ConversationLines`,
`StageDirections`, `StageDirectionTranslations`, `SoundCues`, `SoundCueTranslations`, `Universe`,
`Series`, `CharacterSources`), `Characters.SourceId` correctly dropped, `ImportBatches.ImportedById`
correctly renamed from `ImportedBy`, and the v1.7.2 seed data (788+ quotes) survived the upgrade
intact. This is the first genuine end-to-end proof that a real v1.7.2 installation upgrades cleanly
through the fixed split (Step 1) and both consolidated migrations (Steps 2-3) — every other test in
this milestone verifies pieces of the mechanism against synthetic fixtures; this is the one exercise
of the real thing.

### 6. Audit for any other post-application edits

**Status:** Done, verified 2026-07-29.

Verified by direct text comparison rather than reading `git log -p` diffs by eye (more reliable for
confirming byte-for-byte identity across 14+ intervening commits touching the same file): extracted
each of the 4 surviving migration constants' exact SQL body both from the `v1.7.2` tag and from the
current `HEAD`, and diffed them directly (ignoring only the `private`→`internal` accessibility change
on the Consumer side, made in Step 2 for testability, not content).

| Constant | v1.7.2 location | Result |
|---|---|---|
| `Migration001_InitialSchema` | `src/Quotinator.Engine/Database/QuotinatorMigrations.cs` | Identical |
| `Migration002_ReseedGenres` | same | Identical |
| `Migration003_ImportBatches` | same | Identical |
| `AuditMigrations.CreateAuditEntriesTable` | `src/Quotinator.Data/Database/AuditMigrations.cs` (same path then and now) | Identical |

All 4 confirmed byte-identical to what `main`/v1.7.2 actually shipped. The #56 `System_ImportConflicts`
incident and the #168 `ImportActionMigrations.CreateImportActionsTable` precedent — the known examples
of this class of mistake — are both already inside the set consolidated away in Steps 2-3, so they
don't affect any of these 4 surviving migrations.

### 7. Decide on a permanent fixture

**Status:** Done, decided 2026-07-29.

**Decision: stays a manual per-milestone step, per ADR 009's existing process — no permanent fixture.**
Weighed against a checked-in binary `.db` snapshot (repo bloat, not text-diffable, needs regenerating
every time a new release becomes the "last released" baseline) and a permanent always-on
reconstructable-script test (adds real complexity and runtime cost — a nested git worktree checkout
and build — to the standard test suite, and the target tag still needs manual updating each release
regardless). Step 5's manual process (git worktree at the release tag, a throwaway test to generate
the snapshot, a second throwaway test against the current branch, then delete both) took a few minutes
end to end and produced a clear, strong result — the process ADR 009 already documents is sufficient
on its own. This does not change ADR 009's stated process, so no Revision section is added to that ADR.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | The legacy `SchemaVersion` split correctly seeds both new counters (3/1, not a raw copy of 4) and preserves `AppliedAt` timestamps | Unit test | `DatabaseInitializerTests.InitialiseAsync_LegacyV172SchemaVersionTable_SplitsCorrectlyAndReplaysRemainingMigrations`, implemented 2026-07-28 |
| 2 | ✅ | `System_AuditEntries`/`System_ImportConflicts`/`System_ChangeLog` all exist and are queryable after a real v1.7.2 upgrade | Unit test + Live | Unit test above, plus step 5's live snapshot run and step 8's live Docker T2 run — both confirmed |
| 3 | ✅ | Consumer migrations 4-11 are consolidated into one migration 4; `QuotinatorMigrations.All` has exactly 4 entries | Live (review) + Unit test | Full test suite green after consolidation, 2026-07-28 |
| 4 | ✅ | Data migrations 2-13 are consolidated into one migration 2; `DataOwnedMigrations` has exactly 2 entries | Live (review) + Unit test | Full test suite green after consolidation, 2026-07-29 |
| 5 | ✅ | A real v1.7.2 snapshot upgrades cleanly to current, matching the from-empty incremental and baseline paths exactly | Live | `git worktree add` snapshot + `InitialiseAsync` against current code — no exception, `DataSchemaVersion=2`, `SchemaVersion=4`, every expected table present, data preserved |
| 6 | ✅ | No migration surviving the consolidation (Consumer 1-3, Data 1) was edited after `main` shipped it | Live (review) | Direct text diff of each constant's body, v1.7.2 tag vs. HEAD — all 4 byte-identical |
| 7 | ✅ | A decision on a permanent v1.7.2 fixture is made and documented | Live (review) | Decision: stays manual per-milestone, no change to ADR 009 — see step 7 above |
| 8 | ✅ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — 1341+809+530+... tests, full suite green, 0 warnings, 0 errors |
| 9 | ✅ | T1 — app starts cleanly against a database that went through the real v1.7.2 → current upgrade path | Live (T1) | Developer confirmed via Visual Studio, 2026-07-29 — log shows `applying 1 pending Data migration(s) (version 1 → 2)`, `applying 1 pending App migration(s) (version 3 → 4)`, `schema updated (data v2, app v4)`, 788 quotes/478 sources/2 characters preserved |
| 10 | ✅ | T2 — Docker smoke test against a container whose database was seeded via the v1.7.2 upgrade path | Live (T2) | `docker build`; regenerated v1.7.2 snapshot mounted as the container's own `/data/quotinatordata.db` — startup log shows `applying 1 pending "Data" migration(s) (version 1 → 2)` / `applying 1 pending "App" migration(s) (version 3 → 4)`, final `schema v4 (data v2)`, 788 quotes/478 sources/2 characters preserved; `/health`, `/version`, `/quotes/random`, `/admin/audit` all confirmed working post-upgrade |

---

## Notes

T1 and T2 are both required — this issue is entirely about `DatabaseInitializer`/migration
correctness (`docs/release-verification.md`'s explicit T1 criterion for exactly this class of
change), not only the blanket per-issue rule.

This is now a real code-change issue, not a pure audit — the bug found while writing this revision,
and the developer's consolidation goal, both require touching migration source files directly. Work
through the steps in order: fixing the split logic (step 1) before consolidating either migration
list (steps 2-3) means the consolidated migrations are verified against a correctly-seeded starting
point from the very first test, rather than discovering the seeding bug again later.

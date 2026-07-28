# #155 — Migration review: verify full incremental path from last-shipped v1.7.2 schema

**Status:** In progress (step 2)
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

**Status:** Not started.

Concatenate the 8 migrations' SQL bodies, in their existing order, into one new
`Migration004_ConsolidatedSinceV172` (or similarly named) constant — literal text concatenation,
since they already run sequentially today; combining them into one transaction is strictly safer
(fully atomic), never less so. `QuotinatorMigrations.All` shrinks to 4 entries. Delete the 8 now-dead
migration constants and their doc comments (git history retains them; CLAUDE.md's "never edit an
already-applied migration" doesn't apply since none has ever reached a real release).

### 3. Squash Data migrations 2-13 into one

**Status:** Not started.

Same technique for the 12 Data-owned migrations across `AuditMigrations.cs`, `ImportConflictMigrations
.cs`, `ChangeLogMigrations.cs`, `ImportActionMigrations.cs`, `SourceFileOverrideMigrations.cs` —
concatenate into one new constant, referenced as `DataOwnedMigrations`'s new entry 2. `DataOwnedMigrations`
shrinks to 2 entries. Delete the 12 now-dead constants. Decide during implementation whether the
consolidated SQL lives in one of the existing files (e.g. `AuditMigrations.cs`, since it now owns the
combined step) or a new file — favor whichever keeps `Quotinator.Data.Database`'s file-per-concept
convention least disrupted.

### 4. Update every test/doc that hard-codes the old migration counts or version numbers

**Status:** Not started.

A non-exhaustive but representative list of what needs auditing (`git grep` for literal version
numbers 2 through 13, and for the now-dead constant names, across `tests/` and `docs/`):
- Every `DataOwnedBaseline_And_IncrementalReplay_ProduceIdentical*Schema` test in
  `DatabaseInitializerOwnershipTests.cs` — these compare baseline vs. incremental replay, which still
  works structurally after consolidation, but any test relying on a specific intermediate version
  number (e.g. "migration 8 had X shape before migration 10 widened it") needs rechecking.
- `DatabaseInitializerTests.cs`'s own `Assert.AreEqual(13, db.DataSchemaVersion, ...)`/
  `Assert.AreEqual(11, db.SchemaVersion, ...)`-style assertions — the final version numbers change to
  2 and 4 respectively.
- Any `QuotinatorMigrations.All.Take(N)` partial-migration test setup — `N` no longer means the same
  thing once the list shrinks to 4 entries; each needs re-deriving what partial state it's actually
  trying to simulate and whether that's still expressible with only 4 entries.
- `SqlQueryGuardTests.AggregateQueries_MatchDocumentedInventory`'s documented constant-name inventory,
  if any consolidated constant's name changes.
- `CLAUDE.md`'s Pre-Push Checklist migration-count references (e.g. "Data migrations 2-13 (the
  rename, System_ImportConflicts, ...)" in the T2 smoke-test baseline note) and `docs/smoke-tests.md`
  post-move.
- ADR 002/004/009/012/013 and any *current* (non-historical-plan-doc) documentation referencing
  specific migration version numbers.
- **Historical milestone plan docs under `docs/milestones/` are NOT touched** — they are frozen
  history describing what was true when written, per this project's existing convention.

### 5. Reconstruct and verify against the real v1.7.2 snapshot

**Status:** Not started.

`git worktree add <path> v1.7.2` (never the current branch), run a fresh `InitialiseAsync` against
an empty file to produce the snapshot .db. Copy it, point the current branch's code at it, run
`InitialiseAsync` again (this now exercises the fixed split + the 2 consolidated migrations), and
confirm: no exception, final `DataSchemaVersion = 2`, final `SchemaVersion = 4`,
`System_AuditEntries`/`System_ImportConflicts`/`System_ChangeLog`/`System_ImportActions`/
`System_SourceFileOverrides` and every Consumer-owned table (`Conversations`, `Series`, `Universe`,
etc.) all exist with the expected shape — schema matches what the from-empty incremental-replay and
baseline paths already produce (existing `*_ProduceIdentical*Schema` test family).

### 6. Audit for any other post-application edits

**Status:** Not started.

Cross-check `git log -p` for every migration constant that survives this consolidation unedited
(Consumer 1-3, Data 1) to confirm none of them were themselves edited after `main` shipped them —
the #56 `System_ImportConflicts` incident and the #168 `ImportActionMigrations.CreateImportActionsTable`
precedent are the known examples of this class of mistake, both already inside the set being
consolidated away here, so this step is really about confirming the *4 surviving, truly-frozen*
migrations have never been touched since v1.7.2 shipped them.

### 7. Decide on a permanent fixture

**Status:** Not started.

Per the original issue's requirement 5 and ADR 009's "Consequences" section — decide whether a
checked-in v1.7.2 snapshot (or a reconstructable script) becomes a permanent automated test fixture,
or whether this stays a manual per-milestone step. Document the decision in the closing comment and,
if it changes ADR 009's stated process, add a Revision section to that ADR in the same commit.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | The legacy `SchemaVersion` split correctly seeds both new counters (3/1, not a raw copy of 4) and preserves `AppliedAt` timestamps | Unit test | `DatabaseInitializerTests.InitialiseAsync_LegacyV172SchemaVersionTable_SplitsCorrectlyAndReplaysRemainingMigrations`, implemented 2026-07-28 |
| 2 | ✅ | `System_AuditEntries`/`System_ImportConflicts`/`System_ChangeLog` all exist and are queryable after a real v1.7.2 upgrade | Unit test + Live | Same test as #1 (unit level); step 5's live snapshot run still pending for the full live proof |
| 3 | ❌ | Consumer migrations 4-11 are consolidated into one migration 4; `QuotinatorMigrations.All` has exactly 4 entries | Live (review) + Unit test | Full test suite green after consolidation |
| 4 | ❌ | Data migrations 2-13 are consolidated into one migration 2; `DataOwnedMigrations` has exactly 2 entries | Live (review) + Unit test | Full test suite green after consolidation |
| 5 | ❌ | A real v1.7.2 snapshot upgrades cleanly to current, matching the from-empty incremental and baseline paths exactly | Live | `git worktree add` snapshot + `InitialiseAsync` against current code; diffed against existing `*_ProduceIdentical*Schema` tests |
| 6 | ❌ | No migration surviving the consolidation (Consumer 1-3, Data 1) was edited after `main` shipped it | Live (review) | `git log -p` audit |
| 7 | ❌ | A decision on a permanent v1.7.2 fixture is made and documented | Live (review) | Closing comment on #155; ADR 009 revised if the decision changes it |
| 8 | ❌ | No regression | Unit test | `dotnet test --configuration Release --verbosity normal` — full suite green, 0 warnings, 0 errors |
| 9 | ❌ | T1 — app starts cleanly against a database that went through the real v1.7.2 → current upgrade path | Live (T1) | Developer confirms clean startup against the step-5-produced database specifically |
| 10 | ❌ | T2 — Docker smoke test against a container whose database was seeded via the v1.7.2 upgrade path | Live (T2) | `docker build`; mount/copy the step-5 database into the container's data directory before first start; `docs/smoke-tests.md` passes against it |

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

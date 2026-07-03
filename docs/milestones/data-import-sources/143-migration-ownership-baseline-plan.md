# #143 — Fresh-database baseline schema + Data/Engine migration ownership split

**Status:** Code complete and fully verified 2026-07-03 (amended for exception-free migrations — see "Exception-free migrations" note below) — build clean (0 warnings/errors), full test suite green (630/630), T1 and T2 both confirmed. Ready to close pending user sign-off.
**GitHub issue:** #143
**Tiers required:** T1, T2 — this changes the actual migration/table-creation logic behind `InitialiseAsync`/`Reset`, the same class of change `docs/release-verification.md` already flags as a T1/T2 trigger for #141.
**Depends on:** #141 (`System_`-prefix naming convention) — done, this issue builds directly on it

---

## Problem

1. Every brand-new database replays all 6 numbered migrations in sequence, even though several steps are pointless for a fresh install (migration002 repairs pre-existing bad data; migration003's pre-seed `INSERT`s are `WHERE EXISTS`-guarded no-ops; migration004 creates a table migration006 immediately renames in the same startup).
2. Migration ownership is tangled: `AuditMigrations`' SQL text lives in `Quotinator.Data`, but `Quotinator.Engine`'s `QuotinatorMigrations.All` decides when it runs, interleaved among Engine's own domain migrations.
3. A single shared version counter means "version N" isn't stable — it shifts if either side's migration count changes.

---

## Spec requirements (as designed)

1. `Quotinator.Data`'s `DatabaseInitializer` owns a fixed, internal `DataOwnedMigrations` list (`AuditMigrations.CreateAuditEntriesTable`, `AuditMigrations.RenameAuditEntriesToSystemAuditEntries`) — never passed via constructor, always applied before any consumer-supplied migration
2. Two independent version tables: `System_SchemaVersion` (Data's own migration count) and `System_ConsumerSchemaVersion` (new — the consumer's own migration count), each with stable, locally-numbered history unaffected by the other's list size
3. A database with zero pre-existing tables takes a one-step baseline path: `DataBaselineSql` (creates `System_AuditEntries` directly under its final name) + the consumer-supplied `SchemaBaseline.Sql` (Engine's own domain tables), one row inserted into each version table, no numbered-migration replay
4. An existing (non-empty) database continues through the unchanged incremental path — the two paths never cross
5. ~~`IsKnownMigrationError`'s recovery cases stay keyed to stable local positions~~ — **superseded**, see "Exception-free migrations" note below. Neither case is handled by catching an exception any more: Data's rename-collision case is fixed at the root (Reset never replays Data's migrations at all, so it can't collide), and Engine's duplicate-column case is now a hard failure with no recovery.
6. `Quotinator.Engine`'s migration constant names match their actual local position (`Migration005_ImportBatchTypeUserSeed` → `Migration004_ImportBatchTypeUserSeed`)
7. `IDatabaseInitializer.SchemaVersion` continues to represent the consumer's own migration count (what operators track release-over-release, surfaced in `/api/v1/version` and the startup banner); Data's own count is exposed via a new, separate property
8. Drift-detection tests (Data-side and Engine-side) prove the baseline can never silently diverge from what the numbered migrations actually produce — including CHECK-constraint behavior, which `PRAGMA table_info` doesn't capture
9. ~~`Reset`'s `preserveSchemaVersion` flag preserves both version tables together as one semantic operation~~ — **superseded**: `System_SchemaVersion` (Data's own) is unconditionally untouched by Reset regardless of the flag; `preserveSchemaVersion` now only governs `System_ConsumerSchemaVersion`. See "Exception-free migrations" note below.

---

## Step status

- [x] `DataOwnedMigrations`, `DataBaselineSql` added to `DatabaseInitializer`
- [x] `Sql.Schema` duplicated per-table constants (Data/Consumer variants) + `AnyTableExists`
- [x] `SchemaBaseline` record simplified (`Sql` only, no manually-declared version)
- [x] `ApplyMigrationPhaseAsync` extracted; two-phase `ApplyMigrationsAsync` (Data phase, then Consumer phase)
- [x] `ApplyBaselineAsync` implemented; `forceIncremental` test seam added (`InitialiseForTestingAsync`)
- [x] `DataSchemaVersion` property added (`IDatabaseInitializer`, `DatabaseInitializer`, all no-op/stub implementers updated)
- [x] ~~`IsKnownMigrationError` split into `IsKnownDataMigrationError`/`IsKnownConsumerMigrationError`~~ — superseded, both removed entirely; see "Exception-free migrations" below
- [x] `DropAndRebuildAsync` generalized to both version tables — superseded, now only touches `System_ConsumerSchemaVersion`; see "Exception-free migrations" below
- [x] `QuotinatorMigrations.All` shrunk to 4 entries; `Migration005_ImportBatchTypeUserSeed` renamed to `Migration004_ImportBatchTypeUserSeed`
- [x] `QuotinatorMigrations.Baseline` added (Engine domain tables only)
- [x] `QuotinatorDatabaseInitializer`/`Program.cs` thread the simplified `SchemaBaseline` parameter
- [x] Data-side and Engine-side drift-detection tests (+ CHECK-constraint behavioral assertions)
- [x] New tests: fresh-DB-takes-baseline, existing-DB-still-incremental, no-baseline-fallback, ordering-proof, preserveSchemaVersion-two-tables
- [x] Existing tests fixed: `InitialiseAsync_PartialMigrationState_SelfHealsAndReseeds` (now uses `forceIncremental` seam), `InitialiseAsync_LegacySchemaVersionTable_IsRenamedWithRowsPreserved`/`InitialiseAsync_LegacyAuditEntriesTable_MigratesToSystemAuditEntriesWithRowsPreserved` (reworked `DowngradeToLegacyNamesAsync` for the two-table shape), `Schema_MigrationVersion_IsBumped`/`CreateV2DatabaseAsync` in `ImportBatchesTests.cs` (targets `System_ConsumerSchemaVersion` directly), `SqlQueryGuardTests.AggregateQueries_MatchDocumentedInventory` (renamed/new `Schema.*` constants)
- [x] `CLAUDE.md` migration-policy addendum
- [x] Build clean, full suite green
- [x] **Amendment:** exception-based recovery removed entirely; root-cause fix for Reset/Data collision; fail-loud-and-restore for the consumer anomaly case; mandatory Reset backup added — see "Exception-free migrations" below

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Data's own migrations always apply before any consumer migration | Unit test | `Quotinator.Data.Tests.Database.DatabaseInitializerOwnershipTests.DataOwnedMigrations_AlwaysApplyBeforeConsumerMigrations` |
| 2 | ✅ | Two independent version tables, each with stable local numbering | Unit test | `DatabaseInitializerTests.InitialiseAsync_TrulyEmptyDatabase_TakesBaselinePathNotIncremental` (row counts), `InitialiseAsync_ExistingDatabaseAtVersion3_StillReplaysRemainingConsumerMigrationsIncrementally` (independent counters) |
| 3 | ✅ | Fresh (zero-table) database takes the baseline path, not incremental | Unit test | `DatabaseInitializerTests.InitialiseAsync_TrulyEmptyDatabase_TakesBaselinePathNotIncremental` |
| 4 | ✅ | Existing non-empty database continues through the incremental path unaffected | Unit test | `DatabaseInitializerTests.InitialiseAsync_ExistingDatabaseAtVersion3_StillReplaysRemainingConsumerMigrationsIncrementally` |
| 5 | ✅ | Baseline schema never silently drifts from the numbered migrations (Data side) | Unit test | `Quotinator.Data.Tests.Database.DatabaseInitializerOwnershipTests.DataOwnedBaseline_And_IncrementalReplay_ProduceIdenticalSystemAuditEntriesSchema` |
| 6 | ✅ | Baseline schema never silently drifts from the numbered migrations (Engine side), including CHECK constraints | Unit test | `DatabaseInitializerTests.Baseline_And_IncrementalReplay_ProduceIdenticalEngineSchema`, `Baseline_And_IncrementalReplay_AcceptSameCheckConstraintValues` |
| 7 | ✅ | No baseline defined falls through to full incremental replay | Unit test | `Quotinator.Data.Tests.Database.DatabaseInitializerOwnershipTests.ApplyBaselineAsync_NoConsumerBaselineDefined_FallsThroughToIncremental` |
| 8 | ✅ | `preserveSchemaVersion:true` on Reset preserves `System_ConsumerSchemaVersion`; `System_SchemaVersion` (Data's own) is never touched by Reset regardless of the flag | Unit test | `DatabaseInitializerTests.ResetAsync_PreserveSchemaVersionTrue_KeepsExistingConsumerVersionRows`, `ResetAsync_AnyParameter_NeverTouchesDataSchemaVersion` |
| 9 | ✅ | Build clean | Live | `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s). Confirmed 2026-07-03 |
| 10 | ✅ | All tests pass | Live | `dotnet test --configuration Release --verbosity normal` — 630/630 passed across all test projects, 0 warnings. Confirmed 2026-07-03 |
| 14 | ✅ | A database created before the split (single combined-counter `System_SchemaVersion`, 6 rows) fails loudly on ordinary startup (recorded version doesn't match actual schema) rather than self-healing, with the database left unchanged and an explicit Reset as the sanctioned recovery | Unit test | `DatabaseInitializerTests.InitialiseAsync_PreSplitCombinedCounterDatabase_FailsSafelyAndRequiresExplicitReset` |
| 11 | ✅ | T1: fresh dev database creates schema via baseline in one step; both version tables and `System_AuditEntries` correct; seeding/counts unchanged; `/api/v1/version` reports the consumer's count | Live | Confirmed 2026-07-03 — deleted local `quotinatordata.db`, restarted. Log: `fresh database detected — creating schema directly at baseline (data v2, app v4)` / `schema created at baseline (data v2, app v4)`. `System_SchemaVersion` has exactly 1 row (`Version=2`); `System_ConsumerSchemaVersion` has exactly 1 row (`Version=4`) — one row per table at the final version, not one per historical migration, matching `ApplyBaselineAsync`'s design. Seeding/stats unaffected (788 quotes, 478 sources, 2 characters). |
| 12 | ✅ | T1: default Reset (non-preserving) on an #143-native database now completes with **no exception at all** (the earlier "known rename-collision recovery" path was removed at the root — Data's own migrations are never replayed by Reset, so nothing ever collides); `System_SchemaVersion` untouched, `System_ConsumerSchemaVersion` cleared and replayed cleanly | Live | Confirmed 2026-07-03 (re-run) — `dotnet run --project src/Quotinator.Api`, `POST /api/v1/admin/database/reset` → 200. Log shows a mandatory pre-reset backup (`backing up v4 → ...`), then `applying 4 pending "App" migration(s) (version 0 → 4)` — no `Data` phase line at all, no `Exception thrown:` anywhere. Verified directly via `Quotinator.Tools.DbInspector`: `System_SchemaVersion` still holds its original 2 rows (`AppliedAt=2026-07-03 05:32:24`, unchanged by the reset that ran at `21:06:31`); `System_ConsumerSchemaVersion` holds 4 fresh rows all timestamped `21:06:31`. |
| 15 | ✅ | A genuine, unexpected failure during Reset's migration replay restores the pre-reset backup and rethrows, rather than leaving the database partially rebuilt | Unit test | `Quotinator.Data.Tests.Database.DatabaseInitializerOwnershipTests.ResetAsync_MigrationFailsDuringReplay_RestoresPreResetBackupAndRethrows` |
| 13 | ✅ | T2: fresh Docker container shows identical baseline behavior | Live | Confirmed 2026-07-03 — `docker build -f docker/Dockerfile -t quotinator:local .`, fresh container (no pre-existing volume). Log: `fresh database detected — creating schema directly at baseline (data v2, app v4)` / `schema created at baseline (data v2, app v4)`, seeded 788 quotes. Smoke tests: `/health` 200, `/version` → `schemaVersion: 4`, `/quotes/random` 200, `/quotes/search?q=love` 200. Also exercised `POST /admin/database/reset` inside the container — identical to the local T1 run: mandatory pre-reset backup taken, no `Data` migration phase line, no `Exception thrown:` anywhere, `applying 4 pending "App" migration(s) (version 0 → 4)` → `reset complete`. |

---

## Notes

Design decisions were made interactively with the user across several rounds — see the session transcript for the full reasoning trail. Key resolved questions:
- Data owns a self-contained internal migration list (not constructor-injected) — rejected the alternative of a single shared, manually-renumbered sequence
- Separate version tables per project, not a shared combined counter — preserves stable, unambiguous version numbers per project regardless of the other's migration count changing over time
- A Reset is an acceptable transition path for existing dev databases, since nothing has shipped in a release

`ImportBatchType.System` (a related but separate correction made the same session) is unaffected by this issue — see #58's and #62's plan docs for that correction.

### Implementation notes

- **`TargetVersion` was dropped from `SchemaBaseline` entirely** (not just left optional) — both Data's and the consumer's final version are derived automatically from `DataOwnedMigrations.Count`/`_consumerMigrations.Count` at the point `ApplyBaselineAsync` runs, removing a manual-sync footgun that the original design still had.
- **Existing tests needed more rework than the plan anticipated**, because several already-existing tests implicitly depended on the *old* combined single-counter shape:
  - `ImportBatchesTests.CreateV2DatabaseAsync` originally wrote directly to a legacy-named `SchemaVersion` table to simulate "Engine migrations 1-2 done, 3 pending." Under the new split, that literal table name is exclusively Data's legacy predecessor — writing Engine-history rows there would have been silently misattributed to Data's own counter. Rewrote it to write directly to `System_ConsumerSchemaVersion` (which never had a legacy name — it's new in #143), correctly targeting Engine's own history.
  - `DatabaseInitializerTests.DowngradeToLegacyNamesAsync` originally deleted `System_SchemaVersion WHERE Version = 6` (the old combined scheme's single rename-step row) to force replay. Under baseline, `System_SchemaVersion` only ever has *one* row (Data's own final version, 2) — there's no "row 6" to delete. Reworked to roll the single row back to `Version = 1` (create-only) before renaming tables back to their legacy names, so Data's own migration 2 (the rename) has something real to replay.
  - `InitialiseAsync_PartialMigrationState_SelfHealsAndReseeds` (issue #106 regression guard) originally rolled `System_SchemaVersion` back from a 6-row incremental history. Since a normal `InitialiseAsync()` now takes the *baseline* path on a fresh DB (one row, not one-per-migration), the test had nothing to roll back from. Fixed by seeding via the new `InitialiseForTestingAsync(forceIncremental: true)` seam instead, and retargeting the rollback at `System_ConsumerSchemaVersion` (the table this regression actually concerns — Engine's migration 3, ImportBatchId columns).
- **CHECK-constraint behavioural test needed `PRAGMA foreign_keys = OFF`** — `QuoteGenres.QuoteId` is a FK to `Quotes(Id)`; inserting a probe row to exercise `QuoteGenres.Genre`'s CHECK constraint without seeding a matching `Quotes` row hit `FOREIGN KEY constraint failed` before the CHECK was even evaluated. Disabled FK enforcement for that test's probe inserts, since FK integrity is not what the test is verifying.
- **`Quotinator.Data.Testing.Database.TempDatabase`** (pre-existing helper, previously used only by `Quotinator.Data.Tests.Testing.TempDatabaseTests`) turned out to be a good fit for the new bare-`DatabaseInitializer` ownership tests in `Quotinator.Data.Tests` — used with an empty DDL list (`new TempDatabase([])`) purely for its temp-file lifecycle management, then `DatabaseInitializer` itself does the real schema work via `InitialiseAsync`.
- **Live T1 testing against a genuinely pre-#143 dev database surfaced a real transition edge case**, first handled via a reactive duplicate-column recovery (see below for why that was replaced), later reworked into `DatabaseInitializerTests.InitialiseAsync_PreSplitCombinedCounterDatabase_FailsSafelyAndRequiresExplicitReset`.

### Exception-free migrations (2026-07-03 amendment)

Live T1 testing exposed `Exception thrown: 'Microsoft.Data.Sqlite.SqliteException'` firing on *every* Reset, caught and interpreted via message-matching. On review this was judged unacceptable: catching an exception to infer "this is actually fine" means a genuinely different failure with the same message substring would be silently misclassified and swallowed, with no way to know whether the correct migrations actually applied. This was reworked with two changes:

1. **Root-cause fix, not a check.** Tracing the actual trigger found that `DropAndRebuildAsync` (Reset) was unconditionally wiping `System_SchemaVersion` (Data's own version table) even though Data's migrations only ever concern `System_`-prefixed tables that a Reset never drops in the first place. Reset now never wipes or replays Data's own migration history at all — `System_SchemaVersion` is left completely untouched by a Reset, regardless of `preserveSchemaVersion`. With that fixed, Data migration 2's rename can never collide during a Reset again: not because of a check, but because the code path that could collide never runs. `ApplyMigrationPhaseAsync` lost its `try`/`catch` entirely — a failing migration's transaction rolls back automatically via `using`, and the exception propagates untouched. `IsKnownDataMigrationError`, `OnKnownDataMigrationErrorAsync`, `IsKnownConsumerMigrationError`, and the now-dead `Sql.SystemAudit.DropStrayLegacyAuditEntriesTable`/`Sql.Schema.DeleteAllDataVersions`/`GetAllDataVersions` were all removed.
2. **The narrower, separate consumer-migration-3 "duplicate column" case (a real historical bug, #106, possibly still affecting v1.5.x–v1.6.1 installations upgrading forward) is not fixable the same way** — Reset already drops and recreates those tables cleanly; it only fires when a database's *recorded* version is out of sync with its *actual* schema. Per explicit decision, this is now a hard failure: `ApplyMigrationsAsync` backs up before any pending migration attempt and, on **any** exception (no type/message filtering), restores that backup and rethrows. The database ends up exactly as it was before the attempt; an explicit Reset is the only sanctioned recovery.

Verified against sqlite.org: neither `ALTER TABLE ... RENAME TO` nor `ALTER TABLE ... ADD COLUMN` supports `IF EXISTS`/`IF NOT EXISTS` at any SQLite version — `CLAUDE.md`'s prior claim to the contrary was wrong and has been corrected. Structural metadata checks (`sqlite_master`, `pragma_table_info`) remain reserved for the single existing whole-database-empty check (`Sql.Schema.AnyTableExists`) and nowhere else.

This also closed a real, previously-unnoticed gap: Reset took **no backup at all** before this change (traced: its internal migration replay always started both counters at what looked like "nothing to protect," which happened to be exactly the condition that skipped backup creation) — despite being the most destructive operation in the class. `CreateBackup` now returns the backup path; a new `RestoreBackup` performs the reverse-direction SQLite online backup.

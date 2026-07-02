# #143 — Fresh-database baseline schema + Data/Engine migration ownership split

**Status:** In progress — implementation starting 2026-07-02
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
5. `IsKnownMigrationError`'s recovery cases stay keyed to stable local positions (Data's rename-collision case: position 2; Engine's duplicate-column case: position 3) — no re-keying needed as either list grows, since positions are now local to each list rather than relative to a combined sequence
6. `Quotinator.Engine`'s migration constant names match their actual local position (`Migration005_ImportBatchTypeUserSeed` → `Migration004_ImportBatchTypeUserSeed`)
7. `IDatabaseInitializer.SchemaVersion` continues to represent the consumer's own migration count (what operators track release-over-release, surfaced in `/api/v1/version` and the startup banner); Data's own count is exposed via a new, separate property
8. Drift-detection tests (Data-side and Engine-side) prove the baseline can never silently diverge from what the numbered migrations actually produce — including CHECK-constraint behavior, which `PRAGMA table_info` doesn't capture
9. `Reset`'s `preserveSchemaVersion` flag preserves both version tables together as one semantic operation

---

## Step status

- [ ] `DataOwnedMigrations`, `DataBaselineSql` added to `DatabaseInitializer`
- [ ] `Sql.Schema` duplicated per-table constants (Data/Consumer variants) + `AnyTableExists`
- [ ] `SchemaBaseline` record simplified (`Sql` only, no manually-declared version)
- [ ] `ApplyMigrationPhaseAsync` extracted; two-phase `ApplyMigrationsAsync` (Data phase, then Consumer phase)
- [ ] `ApplyBaselineAsync` implemented; `forceIncremental` test seam added
- [ ] `DataSchemaVersion` property added
- [ ] `IsKnownMigrationError` re-keyed to stable local positions
- [ ] `DropAndRebuildAsync` generalized to both version tables
- [ ] `QuotinatorMigrations.All` shrunk to 4 entries; `Migration004_ImportBatchTypeUserSeed` renamed
- [ ] `QuotinatorMigrations.Baseline` added (Engine domain tables only)
- [ ] `QuotinatorDatabaseInitializer`/`Program.cs` thread the simplified `SchemaBaseline` parameter
- [ ] Data-side and Engine-side drift-detection tests (+ CHECK-constraint behavioral assertions)
- [ ] New tests: fresh-DB-takes-baseline, existing-DB-still-incremental, no-baseline-fallback, ordering-proof, preserveSchemaVersion-two-tables
- [ ] Existing tests fixed: `InitialiseAsync_PartialMigrationState_SelfHealsAndReseeds`, `SchemaVersion == 6` literals updated to `== 4`
- [ ] `CLAUDE.md` migration-policy addendum
- [ ] Build clean, full suite green

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | Data's own migrations always apply before any consumer migration | Unit test | `DataOwnedMigrations_AlwaysApplyBeforeConsumerMigrations` |
| 2 | ⬜ | Two independent version tables, each with stable local numbering | Unit test | TBD during implementation |
| 3 | ⬜ | Fresh (zero-table) database takes the baseline path, not incremental | Unit test | `InitialiseAsync_TrulyEmptyDatabase_TakesBaselinePathNotIncremental` |
| 4 | ⬜ | Existing non-empty database continues through the incremental path unaffected | Unit test | `InitialiseAsync_ExistingDatabaseAtVersion3_StillReplaysRemainingConsumerMigrationsIncrementally` |
| 5 | ⬜ | Baseline schema never silently drifts from the numbered migrations (Data side) | Unit test | Data-side drift test, `Quotinator.Data.Tests` |
| 6 | ⬜ | Baseline schema never silently drifts from the numbered migrations (Engine side), including CHECK constraints | Unit test | Engine-side drift test + CHECK-constraint assertions, `Quotinator.Engine.Tests` |
| 7 | ⬜ | No baseline defined falls through to full incremental replay | Unit test | `ApplyBaselineAsync_NoConsumerBaselineDefined_FallsThroughToIncremental` |
| 8 | ⬜ | `preserveSchemaVersion:true` on Reset preserves both version tables together | Unit test | `PreserveSchemaVersionTrue_PreservesBothVersionTables` |
| 9 | ⬜ | Build clean | Live | `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s) |
| 10 | ⬜ | All tests pass | Live | `dotnet test --configuration Release --verbosity normal` — full suite green, 0 warnings |
| 11 | ⬜ | T1: fresh dev database creates schema via baseline in one step; both version tables and `System_AuditEntries` correct; seeding/counts unchanged; `/api/v1/version` reports the consumer's count | Live | Not yet run |
| 12 | ⬜ | T1: existing dev database (pre-restructuring) requires and correctly handles a Reset to pick up the new two-table structure | Live | Not yet run |
| 13 | ⬜ | T2: fresh Docker container shows identical baseline behavior | Live | Not yet run |

---

## Notes

Design decisions were made interactively with the user across several rounds — see the session transcript for the full reasoning trail. Key resolved questions:
- Data owns a self-contained internal migration list (not constructor-injected) — rejected the alternative of a single shared, manually-renumbered sequence
- Separate version tables per project, not a shared combined counter — preserves stable, unambiguous version numbers per project regardless of the other's migration count changing over time
- A Reset is an acceptable transition path for existing dev databases, since nothing has shipped in a release

`ImportBatchType.System` (a related but separate correction made the same session) is unaffected by this issue — see #58's and #62's plan docs for that correction.

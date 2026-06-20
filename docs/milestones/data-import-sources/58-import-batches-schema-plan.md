# #58 — ImportBatches schema

**Status:** Not started  
**GitHub issue:** #58  
**Depends on:** #71 (generic repository pattern)  
**Unblocks:** #56, #57 (Problem 4), #59, #45 (batch row), #64 (policy recording), #67, #68, #69

---

## Spec requirements

1. New `ImportBatches` table with columns: `Id` (UUID, PK), `Name` (TEXT NOT NULL), `Type` (TEXT NOT NULL — `seed` | `import` | `system`), `Url` (TEXT, nullable), `ImportedAt` (TEXT NOT NULL, ISO 8601 UTC), `ImportedBy` (TEXT, nullable), `RecordCount` (INT NOT NULL DEFAULT 0)
2. Type values: `seed` (external dataset, has a `Url`), `import` (via bulk import endpoint, `ImportedBy` = user UUID), `system` (startup seeding from bundled sources)
3. Nullable `ImportBatchId TEXT REFERENCES ImportBatches(Id)` column on `Quotes`, `Characters`, `Sources`, `People`
4. Pre-seeded batch rows for vilaboim and NikhilNamal17 (Type = `seed`); existing records stay `NULL` — provenance not captured retroactively
5. Seeder creates one `ImportBatch` row per source file before seeding; writes `ImportBatchId` on all inserts for that file
6. Schema migration version bump

---

## Implementation steps

- [ ] Bump schema version in `DatabaseInitializer`
- [ ] Add `ImportBatches` table to schema DDL
- [ ] Add nullable `ImportBatchId TEXT REFERENCES ImportBatches(Id)` column to `Quotes`, `Characters`, `Sources`, `People`
- [ ] Add `ImportBatch` C# record in `Quotinator.Core`
- [ ] Add `IImportBatchRepository` and `SqliteImportBatchRepository` in `Quotinator.Data`
- [ ] Register in DI
- [ ] Update seeder to create one `ImportBatch` row per source file and pass `ImportBatchId` to all insert calls
- [ ] Insert pre-seed rows for vilaboim and NikhilNamal17 on migration (existing records stay `NULL`)
- [ ] Integration tests (see verification table)

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | `ImportBatches` table created with correct columns | Unit test | `ImportBatchesTests.Schema_ImportBatchesTable_HasAllRequiredColumns` |
| 2 | ❌ | Nullable `ImportBatchId` FK on all four entity tables | Unit test | `ImportBatchesTests.Schema_EntityTables_HaveNullableImportBatchIdFK` |
| 3 | ❌ | Pre-seed rows for vilaboim and NikhilNamal17 present after migration | Unit test | `ImportBatchesTests.Seeding_PreSeedBatches_ExistAfterMigration` |
| 4 | ❌ | Seeder creates one `ImportBatch` row per source file | Unit test | `ImportBatchesTests.Seeding_TwoSourceFiles_ProduceTwoDistinctBatches` |
| 5 | ❌ | Existing records retain `NULL` `ImportBatchId` after migration | Unit test | `ImportBatchesTests.Migration_ExistingRecords_HaveNullImportBatchId` |
| 6 | ❌ | Schema migration version bumped | Unit test | `ImportBatchesTests.Schema_MigrationVersion_IsBumped` |

---

## Repository design decision (deferred from #71)

#71 delivered `IRepository<T>` and `SqliteRepository<T>` in `Quotinator.Data`. This issue adds the first concrete repository for `ImportBatch`.

At the start of #58, decide which shape fits:

- **Option A — plain injection:** `ImportBatch` extends `RecordBase`; use `IRepository<ImportBatch>` directly via DI with no subclass. Choose this if the four base methods (`GetByIdAsync`, `InsertAsync`, `UpdateAsync`, `SoftDeleteAsync`) are sufficient.
- **Option B — subclass:** Create `IImportBatchRepository` extending `IRepository<ImportBatch>` and `SqliteImportBatchRepository` extending `SqliteRepository<ImportBatch>`. Choose this if additional query methods are needed (e.g. list all batches, find by type, update `RecordCount`).

Record the decision and reasoning in this plan doc before implementing.

---

## Notes

`RecordCount` is denormalised for display performance; updated after each import run and after targeted resets.

`ImportBatches` is excluded from any export endpoint — it is instance-specific provenance data.

`NULL` `ImportBatchId` means the record predates provenance tracking or was created via the management UI with no associated import.

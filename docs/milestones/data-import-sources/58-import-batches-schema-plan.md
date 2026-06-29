# #58 — ImportBatches schema

**Status:** Closed ✅  
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

- [x] Bump schema version in `DatabaseInitializer`
- [x] Add `ImportBatches` table to schema DDL
- [x] Add nullable `ImportBatchId TEXT REFERENCES ImportBatches(Id)` column to `Quotes`, `Characters`, `Sources`, `People`
- [x] Add `ImportBatch` C# record in `Quotinator.Core`
- [x] Add `IImportBatchRepository` (extends `IRepository<ImportBatch>`) in `Quotinator.Core`
- [x] Add `SqliteImportBatchRepository` (extends `SqliteRepository<ImportBatch>`) in `Quotinator.Core`
- [x] Register `IImportBatchRepository` → `SqliteImportBatchRepository` in DI (switch `DatabaseInitializer` to DI-managed registration so the repository can be injected)
- [x] Update seeder to create one `ImportBatch` row per source file and pass `ImportBatchId` to all insert calls
- [x] Insert pre-seed rows for vilaboim and NikhilNamal17 on migration (existing records stay `NULL`)
- [x] Integration tests (see verification table)

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `ImportBatches` table created with correct columns | Unit test | `ImportBatchesTests.Schema_ImportBatchesTable_HasAllRequiredColumns` |
| 2 | ✅ | Nullable `ImportBatchId` FK on all four entity tables | Unit test | `ImportBatchesTests.Schema_EntityTables_HaveNullableImportBatchIdFK` |
| 3 | ✅ | Pre-seed rows for vilaboim and NikhilNamal17 present after migration | Unit test | `ImportBatchesTests.Seeding_PreSeedBatches_ExistAfterMigration` |
| 4 | ✅ | Seeder creates one `ImportBatch` row per source file | Unit test | `ImportBatchesTests.Seeding_TwoSourceFiles_ProduceTwoDistinctBatches` |
| 5 | ✅ | Existing records retain `NULL` `ImportBatchId` after migration | Unit test | `ImportBatchesTests.Migration_ExistingRecords_HaveNullImportBatchId` |
| 6 | ✅ | Schema migration version bumped | Unit test | `ImportBatchesTests.Schema_MigrationVersion_IsBumped` |

---

## Repository design decision (resolved)

**Decision: Option B — dedicated `IImportBatchRepository`.**

Reason: downstream issues (#59 soft-reset by batch, #60 Blazor batches page) require listing all batches and filtering by type. These are not on the base `IRepository<T>` interface. Using Option A would force raw Dapper calls outside the repository wherever these queries are needed, violating the string centralisation policy.

```csharp
// Quotinator.Core
public interface IImportBatchRepository : IRepository<ImportBatch>
{
    Task<IReadOnlyList<ImportBatch>> GetAllAsync(IUnitOfWork? unitOfWork = null);
    Task<IReadOnlyList<ImportBatch>> GetByTypeAsync(ImportBatchType type, IUnitOfWork? unitOfWork = null);
    Task UpdateRecordCountAsync(Guid id, int count, IUnitOfWork? unitOfWork = null);
}

public sealed class SqliteImportBatchRepository : SqliteRepository<ImportBatch>, IImportBatchRepository
{
    // additional methods via Dapper
}
```

Both the interface and implementation live in `Quotinator.Core` — not `Quotinator.Data`. `Quotinator.Data` has no reference to `Quotinator.Core`; putting a concrete entity repository there would create a circular dependency.

`DatabaseInitializer` switches from `new` (in `Program.cs`) to full DI registration so `IImportBatchRepository` can be injected via constructor — per the DI policy in `CLAUDE.md`.

---

## Notes

`RecordCount` is denormalised for display performance; updated after each import run and after targeted resets.

`ImportBatches` is excluded from any export endpoint — it is instance-specific provenance data.

`NULL` `ImportBatchId` means the record predates provenance tracking or was created via the management UI with no associated import.

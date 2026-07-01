# #58 — ImportBatches schema

**Status:** Issue closed ✅ — post-closure regression fix pending release  
**GitHub issue:** #58  
**Tiers required:** T1, T2  
**Depends on:** #71 (generic repository pattern)  
**Unblocks:** #56, #57 (Problem 4), #59, #45 (batch row), #64 (policy recording), #67, #68, #69

**Note (2026-06-30):** items 7–8 in the verification table cover a regression found *after* #58 was closed (`Type`/`Url` hardcoded wrong on `ImportBatch` rows). The fix is committed on `feature/data-import-sources` with T1+T2 verified, but has not shipped in any release yet. This is "pending release", not "done" — no further closing action applies since #58 is already closed; this is tracked here purely as a verification record for the fix itself.

---

## Spec requirements

1. New `ImportBatches` table with columns: `Id` (UUID, PK), `Name` (TEXT NOT NULL), `Type` (TEXT NOT NULL — `seed` | `import` | `system` | `user-seed`), `Url` (TEXT, nullable), `ImportedAt` (TEXT NOT NULL, ISO 8601 UTC), `ImportedBy` (TEXT, nullable), `RecordCount` (INT NOT NULL DEFAULT 0)
2. Type values: `seed` (our own bundled external dataset, has a `Url`/`github` object), `system` (fixed/predetermined bundled data with no `Url`, e.g. curated quotes), `user-seed` (file scanned from the user's imports folder at startup, regardless of `Url`), `import` (via bulk import endpoint, `ImportedBy` = user UUID). **`user-seed` added 2026-07-01 (#62) — see that plan doc for the full rationale and the `SeedBatchOrigin` mechanism that determines it.** Conceptually, `system` is meant to survive reseed/reset while `seed`/`user-seed`/`import` are replaceable content that gets cleared — that preservation *behavior* is not yet implemented; tracked as a follow-up issue under milestone #10.
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
| 4 | ✅ | Seeder creates one `ImportBatch` row per source file | Unit test | `ImportBatchesTests.Seeding_TwoSourceFiles_ProduceTwoDistinctBatchesWithCorrectTypes` |
| 5 | ✅ | Existing records retain `NULL` `ImportBatchId` after migration | Unit test | `ImportBatchesTests.Migration_ExistingRecords_HaveNullImportBatchId` |
| 6 | ✅ | Schema migration version bumped | Unit test | `ImportBatchesTests.Schema_MigrationVersion_IsBumped` |
| 7 | ✅ | Seeder sets correct `Type`/`Url` per file (Seed when manifest has a `url`, System otherwise) | Unit test | `ImportBatchesTests.Seeding_TwoSourceFiles_ProduceTwoDistinctBatchesWithCorrectTypes` |
| 8 | ✅ | Fresh seed in Visual Studio produces correct `Type`/`Url` per row | Live | Start app in VS with an empty database; inspect `ImportBatches` table; confirm curated→`System`/`NULL`, vilaboim/NikhilNamal17→`Seed`/GitHub URL. Confirmed 2026-06-30 |
| 9 | ✅ | Docker build/publish includes updated `manifest.json`; seeding behaves identically in container | Live | `docker build -f docker/Dockerfile -t quotinator:local .`; run; confirm container log shows same counts as T1 (788/478/2/0); `/api/v1/health`, `/api/v1/version`, `/api/v1/quotes/random` return expected output. Confirmed 2026-06-30 |

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
